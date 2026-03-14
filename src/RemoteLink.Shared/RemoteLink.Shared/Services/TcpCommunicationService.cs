using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Security;

namespace RemoteLink.Shared.Services;

/// <summary>
/// TCP-based implementation of <see cref="ICommunicationService"/>.
/// Supports both host (server) and client modes:
/// <list type="bullet">
/// <item><description>Call <see cref="StartAsync"/> to listen for incoming connections (host/server mode).</description></item>
/// <item><description>Call <see cref="ConnectToDeviceAsync"/> to connect to a host (client mode).</description></item>
/// </list>
/// Wire format: 4-byte little-endian payload length, followed by 1-byte message-type tag,
/// followed by the raw payload bytes.  Messages carrying binary blobs (ScreenData, AudioData,
/// FileTransferChunk) are encoded with BinaryWriter so byte arrays are written verbatim —
/// no Base64 overhead.  All other messages use direct UTF-8 JSON serialization with no
/// envelope wrapper.
/// </summary>
public class TcpCommunicationService : ICommunicationService, IDisposable
{
    // ── Message type tags ────────────────────────────────────────────────────

    private const byte MsgTypeScreen                  = 0x01;
    private const byte MsgTypeInput                   = 0x02;
    private const byte MsgTypePairingRequest          = 0x03;
    private const byte MsgTypePairingResponse         = 0x04;
    private const byte MsgTypeConnectionQuality       = 0x05;
    private const byte MsgTypeSessionControlRequest   = 0x06;
    private const byte MsgTypeSessionControlResponse  = 0x07;
    private const byte MsgTypeClipboard               = 0x08;
    private const byte MsgTypeFileTransferRequest     = 0x09;
    private const byte MsgTypeFileTransferResponse    = 0x0A;
    private const byte MsgTypeFileTransferChunk       = 0x0B;
    private const byte MsgTypeFileTransferComplete    = 0x0C;
    private const byte MsgTypeAudio                   = 0x0D;
    private const byte MsgTypeChatMessage             = 0x0E;
    private const byte MsgTypeMessageRead             = 0x0F;
    private const byte MsgTypePrintJob                = 0x10;
    private const byte MsgTypePrintJobResponse        = 0x11;
    private const byte MsgTypePrintJobStatus          = 0x12;

    // ── State ────────────────────────────────────────────────────────────────

    private readonly TlsConfiguration _tlsConfig;
    private TcpListener? _listener;
    private TcpClient? _activeClient;
    private NetworkStream? _activeStream;
    private SslStream? _activeSslStream;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _acceptTask;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="TcpCommunicationService"/>.
    /// </summary>
    /// <param name="tlsConfig">
    /// TLS configuration. If <c>null</c>, uses default settings
    /// (TLS enabled with self-signed certificates accepted).
    /// </param>
    public TcpCommunicationService(TlsConfiguration? tlsConfig = null)
    {
        _tlsConfig = tlsConfig ?? TlsConfiguration.CreateDefault();
    }

    // ── ICommunicationService ────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsConnected => _activeClient?.Connected == true && _activeStream != null;

    /// <inheritdoc/>
    public event EventHandler<ScreenData>? ScreenDataReceived;

    /// <inheritdoc/>
    public event EventHandler<InputEvent>? InputEventReceived;

    /// <inheritdoc/>
    public event EventHandler<bool>? ConnectionStateChanged;

    /// <inheritdoc/>
    public event EventHandler<PairingRequest>? PairingRequestReceived;

    /// <inheritdoc/>
    public event EventHandler<PairingResponse>? PairingResponseReceived;

    /// <inheritdoc/>
    public event EventHandler<ConnectionQuality>? ConnectionQualityReceived;

    /// <inheritdoc/>
    public event EventHandler<SessionControlRequest>? SessionControlRequestReceived;

    /// <inheritdoc/>
    public event EventHandler<SessionControlResponse>? SessionControlResponseReceived;

    /// <inheritdoc/>
    public event EventHandler<ClipboardData>? ClipboardDataReceived;

    /// <inheritdoc/>
    public event EventHandler<FileTransferRequest>? FileTransferRequestReceived;

    /// <inheritdoc/>
    public event EventHandler<FileTransferResponse>? FileTransferResponseReceived;

    /// <inheritdoc/>
    public event EventHandler<FileTransferChunk>? FileTransferChunkReceived;

    /// <inheritdoc/>
    public event EventHandler<FileTransferComplete>? FileTransferCompleteReceived;

    /// <inheritdoc/>
    public event EventHandler<AudioData>? AudioDataReceived;

    /// <inheritdoc/>
    public event EventHandler<ChatMessage>? ChatMessageReceived;

    /// <inheritdoc/>
    public event EventHandler<string>? MessageReadReceived;

    /// <inheritdoc/>
    public event EventHandler<PrintJob>? PrintJobReceived;

    /// <inheritdoc/>
    public event EventHandler<PrintJobResponse>? PrintJobResponseReceived;

    /// <inheritdoc/>
    public event EventHandler<PrintJobStatus>? PrintJobStatusReceived;

    // ── Server / host mode ───────────────────────────────────────────────────

    /// <summary>
    /// Start listening for incoming TCP connections on <paramref name="port"/>.
    /// Accepts one client at a time; subsequent clients are queued by the OS.
    /// </summary>
    public async Task StartAsync(int port)
    {
        _cts = new CancellationTokenSource();

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        Console.WriteLine($"[TCP] Listening on port {port}…");

        _acceptTask = AcceptLoopAsync(_cts.Token);

        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                Console.WriteLine($"[TCP] Client connected from {client.Client.RemoteEndPoint}");

                // Drop any previous connection
                await DropConnectionAsync();

                _activeClient = client;
                _activeStream = client.GetStream();

                // ── TLS handshake (server mode) ──────────────────────────────
                if (_tlsConfig.Enabled)
                {
                    try
                    {
                        var cert = _tlsConfig.ServerCertificate
                            ?? TlsConfiguration.GenerateSelfSignedCertificate("CN=RemoteLink Host");

                        var sslStream = new SslStream(
                            _activeStream,
                            leaveInnerStreamOpen: false,
                            userCertificateValidationCallback: ValidateClientCertificate);

                        await sslStream.AuthenticateAsServerAsync(
                            cert,
                            clientCertificateRequired: false,
                            enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
                            checkCertificateRevocation: false);

                        _activeSslStream = sslStream;
                        Console.WriteLine("[TLS] Server handshake complete");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TLS] Server handshake failed: {ex.Message}");
                        await DropConnectionAsync();
                        continue;
                    }
                }

                ConnectionStateChanged?.Invoke(this, true);

                var readStream = (_activeSslStream as Stream) ?? _activeStream;
                _receiveTask = ReceiveLoopAsync(readStream, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP] Accept error: {ex.Message}");
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
    }

    // ── Client mode ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> ConnectToDeviceAsync(DeviceInfo device)
    {
        if (string.IsNullOrWhiteSpace(device.IPAddress))
        {
            Console.WriteLine("[TCP] ConnectToDevice: no IP address on DeviceInfo.");
            return false;
        }

        try
        {
            _cts ??= new CancellationTokenSource();

            await DropConnectionAsync();

            var client = new TcpClient();
            using var connectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(device.IPAddress, device.Port, connectTimeoutCts.Token);

            _activeClient = client;
            _activeStream = client.GetStream();

            Console.WriteLine($"[TCP] Connected to {device.DeviceName} at {device.IPAddress}:{device.Port}");

            // ── TLS handshake (client mode) ──────────────────────────────────
            if (_tlsConfig.Enabled)
            {
                try
                {
                    var sslStream = new SslStream(
                        _activeStream,
                        leaveInnerStreamOpen: false,
                        userCertificateValidationCallback: ValidateServerCertificate);

                    var targetHost = _tlsConfig.TargetHost ?? device.IPAddress;

                    await sslStream.AuthenticateAsClientAsync(
                        targetHost,
                        clientCertificates: null,
                        enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
                        checkCertificateRevocation: false);

                    _activeSslStream = sslStream;
                    Console.WriteLine("[TLS] Client handshake complete");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TLS] Client handshake failed: {ex.Message}");
                    await DropConnectionAsync();
                    return false;
                }
            }

            ConnectionStateChanged?.Invoke(this, true);

            var readStream = (_activeSslStream as Stream) ?? _activeStream;
            _receiveTask = ReceiveLoopAsync(readStream, _cts.Token);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP] Connection failed: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        await DropConnectionAsync();
    }

    // ── Send ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task SendScreenDataAsync(ScreenData screenData)
        => await SendRawAsync(MsgTypeScreen, EncodeScreenData(screenData));

    /// <inheritdoc/>
    public async Task SendInputEventAsync(InputEvent inputEvent)
        => await SendRawAsync(MsgTypeInput, JsonSerializer.SerializeToUtf8Bytes(inputEvent));

    /// <inheritdoc/>
    public async Task SendPairingRequestAsync(PairingRequest request)
        => await SendRawAsync(MsgTypePairingRequest, JsonSerializer.SerializeToUtf8Bytes(request));

    /// <inheritdoc/>
    public async Task SendPairingResponseAsync(PairingResponse response)
        => await SendRawAsync(MsgTypePairingResponse, JsonSerializer.SerializeToUtf8Bytes(response));

    /// <inheritdoc/>
    public async Task SendConnectionQualityAsync(ConnectionQuality quality)
        => await SendRawAsync(MsgTypeConnectionQuality, JsonSerializer.SerializeToUtf8Bytes(quality));

    /// <inheritdoc/>
    public async Task SendSessionControlRequestAsync(SessionControlRequest request)
        => await SendRawAsync(MsgTypeSessionControlRequest, JsonSerializer.SerializeToUtf8Bytes(request));

    /// <inheritdoc/>
    public async Task SendSessionControlResponseAsync(SessionControlResponse response)
        => await SendRawAsync(MsgTypeSessionControlResponse, JsonSerializer.SerializeToUtf8Bytes(response));

    /// <inheritdoc/>
    public async Task SendClipboardDataAsync(ClipboardData clipboardData)
        => await SendRawAsync(MsgTypeClipboard, JsonSerializer.SerializeToUtf8Bytes(clipboardData));

    /// <inheritdoc/>
    public async Task SendFileTransferRequestAsync(FileTransferRequest request)
        => await SendRawAsync(MsgTypeFileTransferRequest, JsonSerializer.SerializeToUtf8Bytes(request));

    /// <inheritdoc/>
    public async Task SendFileTransferResponseAsync(FileTransferResponse response)
        => await SendRawAsync(MsgTypeFileTransferResponse, JsonSerializer.SerializeToUtf8Bytes(response));

    /// <inheritdoc/>
    public async Task SendFileTransferChunkAsync(FileTransferChunk chunk)
        => await SendRawAsync(MsgTypeFileTransferChunk, EncodeFileTransferChunk(chunk));

    /// <inheritdoc/>
    public async Task SendFileTransferCompleteAsync(FileTransferComplete complete)
        => await SendRawAsync(MsgTypeFileTransferComplete, JsonSerializer.SerializeToUtf8Bytes(complete));

    /// <inheritdoc/>
    public async Task SendAudioDataAsync(AudioData audioData)
        => await SendRawAsync(MsgTypeAudio, EncodeAudioData(audioData));

    /// <inheritdoc/>
    public async Task SendChatMessageAsync(ChatMessage message)
        => await SendRawAsync(MsgTypeChatMessage, JsonSerializer.SerializeToUtf8Bytes(message));

    /// <inheritdoc/>
    public async Task SendMessageReadAsync(string messageId)
        => await SendRawAsync(MsgTypeMessageRead, JsonSerializer.SerializeToUtf8Bytes(messageId));

    /// <inheritdoc/>
    public async Task SendPrintJobAsync(PrintJob printJob)
        => await SendRawAsync(MsgTypePrintJob, JsonSerializer.SerializeToUtf8Bytes(printJob));

    /// <inheritdoc/>
    public async Task SendPrintJobResponseAsync(PrintJobResponse response)
        => await SendRawAsync(MsgTypePrintJobResponse, JsonSerializer.SerializeToUtf8Bytes(response));

    /// <inheritdoc/>
    public async Task SendPrintJobStatusAsync(PrintJobStatus status)
        => await SendRawAsync(MsgTypePrintJobStatus, JsonSerializer.SerializeToUtf8Bytes(status));

    // ── Stop ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        _cts?.Cancel();

        await DropConnectionAsync();

        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener = null;

        // Wait for background tasks
        if (_acceptTask != null)
        {
            try { await _acceptTask; } catch { /* expected */ }
            _acceptTask = null;
        }

        Console.WriteLine("[TCP] Service stopped.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            // 5-byte frame header: [4-byte LE payload-length][1-byte msg-type]
            var headerBuf = new byte[5];

            while (!ct.IsCancellationRequested)
            {
                int read = await ReadExactAsync(stream, headerBuf, 0, 5, ct);
                if (read < 5)
                {
                    Console.WriteLine("[TCP] Connection closed by remote.");
                    break;
                }

                int payloadLen = BinaryPrimitives.ReadInt32LittleEndian(headerBuf);
                byte msgType = headerBuf[4];

                if (payloadLen < 0 || payloadLen > 64 * 1024 * 1024) // 64 MB guard
                {
                    Console.WriteLine($"[TCP] Invalid payload length {payloadLen}; closing.");
                    break;
                }

                byte[] payload = new byte[payloadLen];
                read = await ReadExactAsync(stream, payload, 0, payloadLen, ct);
                if (read < payloadLen)
                {
                    Console.WriteLine("[TCP] Incomplete message; closing.");
                    break;
                }

                DispatchMessage(msgType, payload);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP] Receive error: {ex.Message}");
        }
        finally
        {
            ConnectionStateChanged?.Invoke(this, false);
        }
    }

    private void DispatchMessage(byte msgType, byte[] payload)
    {
        try
        {
            switch (msgType)
            {
                case MsgTypeScreen:
                    ScreenDataReceived?.Invoke(this, DecodeScreenData(payload));
                    break;

                case MsgTypeInput:
                    var ie = JsonSerializer.Deserialize<InputEvent>(payload);
                    if (ie != null) InputEventReceived?.Invoke(this, ie);
                    break;

                case MsgTypePairingRequest:
                    var pr = JsonSerializer.Deserialize<PairingRequest>(payload);
                    if (pr != null) PairingRequestReceived?.Invoke(this, pr);
                    break;

                case MsgTypePairingResponse:
                    var prr = JsonSerializer.Deserialize<PairingResponse>(payload);
                    if (prr != null) PairingResponseReceived?.Invoke(this, prr);
                    break;

                case MsgTypeConnectionQuality:
                    var cq = JsonSerializer.Deserialize<ConnectionQuality>(payload);
                    if (cq != null) ConnectionQualityReceived?.Invoke(this, cq);
                    break;

                case MsgTypeSessionControlRequest:
                    var scr = JsonSerializer.Deserialize<SessionControlRequest>(payload);
                    if (scr != null) SessionControlRequestReceived?.Invoke(this, scr);
                    break;

                case MsgTypeSessionControlResponse:
                    var scresp = JsonSerializer.Deserialize<SessionControlResponse>(payload);
                    if (scresp != null) SessionControlResponseReceived?.Invoke(this, scresp);
                    break;

                case MsgTypeClipboard:
                    var cd = JsonSerializer.Deserialize<ClipboardData>(payload);
                    if (cd != null) ClipboardDataReceived?.Invoke(this, cd);
                    break;

                case MsgTypeFileTransferRequest:
                    var ftr = JsonSerializer.Deserialize<FileTransferRequest>(payload);
                    if (ftr != null) FileTransferRequestReceived?.Invoke(this, ftr);
                    break;

                case MsgTypeFileTransferResponse:
                    var ftresp = JsonSerializer.Deserialize<FileTransferResponse>(payload);
                    if (ftresp != null) FileTransferResponseReceived?.Invoke(this, ftresp);
                    break;

                case MsgTypeFileTransferChunk:
                    FileTransferChunkReceived?.Invoke(this, DecodeFileTransferChunk(payload));
                    break;

                case MsgTypeFileTransferComplete:
                    var ftcomplete = JsonSerializer.Deserialize<FileTransferComplete>(payload);
                    if (ftcomplete != null) FileTransferCompleteReceived?.Invoke(this, ftcomplete);
                    break;

                case MsgTypeAudio:
                    AudioDataReceived?.Invoke(this, DecodeAudioData(payload));
                    break;

                case MsgTypeChatMessage:
                    var cm = JsonSerializer.Deserialize<ChatMessage>(payload);
                    if (cm != null) ChatMessageReceived?.Invoke(this, cm);
                    break;

                case MsgTypeMessageRead:
                    var mr = JsonSerializer.Deserialize<string>(payload);
                    if (mr != null) MessageReadReceived?.Invoke(this, mr);
                    break;

                case MsgTypePrintJob:
                    var pj = JsonSerializer.Deserialize<PrintJob>(payload);
                    if (pj != null) PrintJobReceived?.Invoke(this, pj);
                    break;

                case MsgTypePrintJobResponse:
                    var pjr = JsonSerializer.Deserialize<PrintJobResponse>(payload);
                    if (pjr != null) PrintJobResponseReceived?.Invoke(this, pjr);
                    break;

                case MsgTypePrintJobStatus:
                    var pjs = JsonSerializer.Deserialize<PrintJobStatus>(payload);
                    if (pjs != null) PrintJobStatusReceived?.Invoke(this, pjs);
                    break;

                default:
                    Console.WriteLine($"[TCP] Unknown message type: 0x{msgType:X2}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP] Dispatch error: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes a framed message: [4-byte LE payload-length][1-byte type][payload].
    /// </summary>
    private async Task SendRawAsync(byte msgType, byte[] payload)
    {
        var writeStream = (_activeSslStream as Stream) ?? _activeStream;

        if (writeStream == null)
        {
            Console.WriteLine("[TCP] SendMessage: not connected.");
            return;
        }

        await _writeLock.WaitAsync();
        try
        {
            var header = new byte[5];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            header[4] = msgType;

            await writeStream.WriteAsync(header);
            await writeStream.WriteAsync(payload);
            await writeStream.FlushAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP] Send error: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task DropConnectionAsync()
    {
        var sslStream = Interlocked.Exchange(ref _activeSslStream, null);
        sslStream?.Dispose();

        var stream = Interlocked.Exchange(ref _activeStream, null);
        stream?.Dispose();

        var client = Interlocked.Exchange(ref _activeClient, null);
        client?.Dispose();

        if (_receiveTask != null)
        {
            try { await _receiveTask; } catch { /* expected */ }
            _receiveTask = null;
        }
    }

    // ── Binary codec: ScreenData ─────────────────────────────────────────────

    private static byte[] EncodeScreenData(ScreenData sd)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        w.Write(sd.FrameId ?? string.Empty);
        w.Write(sd.Timestamp.ToUniversalTime().Ticks);
        w.Write(sd.Width);
        w.Write(sd.Height);
        w.Write((byte)sd.Format);
        w.Write(sd.Quality);
        w.Write(sd.IsDelta);
        w.Write(sd.ReferenceFrameId ?? string.Empty);

        var regions = sd.DeltaRegions;
        w.Write(regions?.Count ?? 0);
        if (regions != null)
        {
            foreach (var r in regions)
            {
                w.Write(r.X);
                w.Write(r.Y);
                w.Write(r.Width);
                w.Write(r.Height);
                w.Write(r.DataOffset);
                w.Write(r.DataLength);
            }
        }

        w.Write(sd.ImageData?.Length ?? 0);
        if (sd.ImageData is { Length: > 0 })
            w.Write(sd.ImageData);

        return ms.ToArray();
    }

    private static ScreenData DecodeScreenData(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var sd = new ScreenData
        {
            FrameId    = r.ReadString(),
            Timestamp  = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
            Width      = r.ReadInt32(),
            Height     = r.ReadInt32(),
            Format     = (ScreenDataFormat)r.ReadByte(),
            Quality    = r.ReadInt32(),
            IsDelta    = r.ReadBoolean()
        };

        var refId = r.ReadString();
        sd.ReferenceFrameId = refId.Length > 0 ? refId : null;

        int regionCount = r.ReadInt32();
        if (regionCount > 0)
        {
            sd.DeltaRegions = new List<DeltaRegion>(regionCount);
            for (int i = 0; i < regionCount; i++)
            {
                sd.DeltaRegions.Add(new DeltaRegion
                {
                    X          = r.ReadInt32(),
                    Y          = r.ReadInt32(),
                    Width      = r.ReadInt32(),
                    Height     = r.ReadInt32(),
                    DataOffset = r.ReadInt32(),
                    DataLength = r.ReadInt32()
                });
            }
        }

        int imageLen = r.ReadInt32();
        sd.ImageData = imageLen > 0 ? r.ReadBytes(imageLen) : Array.Empty<byte>();

        return sd;
    }

    // ── Binary codec: AudioData ──────────────────────────────────────────────

    private static byte[] EncodeAudioData(AudioData ad)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        w.Write(ad.SampleRate);
        w.Write(ad.Channels);
        w.Write(ad.BitsPerSample);
        w.Write(ad.Timestamp.ToUniversalTime().Ticks);
        w.Write(ad.DurationMs);
        w.Write(ad.Format ?? string.Empty);

        w.Write(ad.Data?.Length ?? 0);
        if (ad.Data is { Length: > 0 })
            w.Write(ad.Data);

        return ms.ToArray();
    }

    private static AudioData DecodeAudioData(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var ad = new AudioData
        {
            SampleRate    = r.ReadInt32(),
            Channels      = r.ReadInt32(),
            BitsPerSample = r.ReadInt32(),
            Timestamp     = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
            DurationMs    = r.ReadInt32(),
            Format        = r.ReadString()
        };

        int dataLen = r.ReadInt32();
        ad.Data = dataLen > 0 ? r.ReadBytes(dataLen) : Array.Empty<byte>();

        return ad;
    }

    // ── Binary codec: FileTransferChunk ──────────────────────────────────────

    private static byte[] EncodeFileTransferChunk(FileTransferChunk chunk)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        w.Write(chunk.TransferId ?? string.Empty);
        w.Write(chunk.Offset);
        w.Write(chunk.Length);
        w.Write(chunk.IsLastChunk);

        w.Write(chunk.Data?.Length ?? 0);
        if (chunk.Data is { Length: > 0 })
            w.Write(chunk.Data);

        return ms.ToArray();
    }

    private static FileTransferChunk DecodeFileTransferChunk(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var chunk = new FileTransferChunk
        {
            TransferId  = r.ReadString(),
            Offset      = r.ReadInt64(),
            Length      = r.ReadInt32(),
            IsLastChunk = r.ReadBoolean()
        };

        int dataLen = r.ReadInt32();
        chunk.Data = dataLen > 0 ? r.ReadBytes(dataLen) : Array.Empty<byte>();

        return chunk;
    }

    // ── Certificate validation ────────────────────────────────────────────────

    private bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // If configured to validate, enforce proper cert checks
        if (_tlsConfig.ValidateRemoteCertificate)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            Console.WriteLine($"[TLS] Server certificate validation failed: {sslPolicyErrors}");
            return false;
        }

        // For local network use, accept self-signed certificates
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        // Allow self-signed or untrusted root for LAN scenarios
        if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
        {
            Console.WriteLine("[TLS] Accepting self-signed server certificate for local network");
            return true;
        }

        Console.WriteLine($"[TLS] Server certificate policy error: {sslPolicyErrors}");
        return false;
    }

    private bool ValidateClientCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // Client certificates are optional in this implementation
        // (pairing is done via PIN, not mutual TLS)
        return true;
    }

    private static async Task<int> ReadExactAsync(
        Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read == 0) break; // remote closed
            totalRead += read;
        }
        return totalRead;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
        _writeLock.Dispose();
    }
}
