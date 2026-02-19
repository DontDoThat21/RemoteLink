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
/// Wire format: 4-byte little-endian message length prefix followed by UTF-8 JSON payload.
/// </summary>
public class TcpCommunicationService : ICommunicationService, IDisposable
{
    // ── Wire message envelope ────────────────────────────────────────────────

    private sealed class NetworkMessage
    {
        public string MessageType { get; set; } = string.Empty;
        /// <summary>Base-64-encoded JSON of the actual payload object.</summary>
        public string Payload { get; set; } = string.Empty;
    }

    private const string MsgTypeScreen = "ScreenData";
    private const string MsgTypeInput = "InputEvent";
    private const string MsgTypePairingRequest = "PairingRequest";
    private const string MsgTypePairingResponse = "PairingResponse";
    private const string MsgTypeConnectionQuality = "ConnectionQuality";
    private const string MsgTypeClipboard = "ClipboardData";
    private const string MsgTypeFileTransferRequest = "FileTransferRequest";
    private const string MsgTypeFileTransferResponse = "FileTransferResponse";
    private const string MsgTypeFileTransferChunk = "FileTransferChunk";
    private const string MsgTypeFileTransferComplete = "FileTransferComplete";

    // ── State ────────────────────────────────────────────────────────────────

    private readonly TlsConfiguration _tlsConfig;
    private TcpListener? _listener;
    private TcpClient? _activeClient;
    private NetworkStream? _activeStream;
    private SslStream? _activeSslStream;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _acceptTask;
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
    public event EventHandler<ClipboardData>? ClipboardDataReceived;

    /// <inheritdoc/>
    public event EventHandler<FileTransferRequest>? FileTransferRequestReceived;

    /// <inheritdoc/>
    public event EventHandler<FileTransferResponse>? FileTransferResponseReceived;

    /// <inheritdoc/>
    public event EventHandler<FileTransferChunk>? FileTransferChunkReceived;

    /// <inheritdoc/>
    public event EventHandler<FileTransferComplete>? FileTransferCompleteReceived;

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
            await client.ConnectAsync(device.IPAddress, device.Port, _cts.Token);

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
    {
        var msg = new NetworkMessage
        {
            MessageType = MsgTypeScreen,
            Payload = Encode(screenData)
        };
        await SendMessageAsync(msg);
    }

    /// <inheritdoc/>
    public async Task SendInputEventAsync(InputEvent inputEvent)
    {
        var msg = new NetworkMessage
        {
            MessageType = MsgTypeInput,
            Payload = Encode(inputEvent)
        };
        await SendMessageAsync(msg);
    }

    /// <inheritdoc/>
    public async Task SendPairingRequestAsync(PairingRequest request)
    {
        var msg = new NetworkMessage
        {
            MessageType = MsgTypePairingRequest,
            Payload = Encode(request)
        };
        await SendMessageAsync(msg);
    }

    /// <inheritdoc/>
    public async Task SendPairingResponseAsync(PairingResponse response)
    {
        var msg = new NetworkMessage
        {
            MessageType = MsgTypePairingResponse,
            Payload = Encode(response)
        };
        await SendMessageAsync(msg);
    }

    /// <inheritdoc/>
    public async Task SendConnectionQualityAsync(ConnectionQuality quality)
    {
        var msg = new NetworkMessage
        {
            MessageType = MsgTypeConnectionQuality,
            Payload = Encode(quality)
        };
        await SendMessageAsync(msg);
    }

    /// <inheritdoc/>
    public async Task SendClipboardDataAsync(ClipboardData clipboardData)
    {
        var msg = new NetworkMessage
        {
            MessageType = MsgTypeClipboard,
            Payload = Encode(clipboardData)
        };
        await SendMessageAsync(msg);
    }

    /// <inheritdoc/>
    public async Task SendFileTransferRequestAsync(FileTransferRequest request)
    {
        var msg = new NetworkMessage
        {
            MessageType = MsgTypeFileTransferRequest,
            Payload = Encode(request)
        };
        await SendMessageAsync(msg);
    }

    /// <inheritdoc/>
    public async Task SendFileTransferResponseAsync(FileTransferResponse response)
    {
        var msg = new NetworkMessage
        {
            MessageType = MsgTypeFileTransferResponse,
            Payload = Encode(response)
        };
        await SendMessageAsync(msg);
    }

    /// <inheritdoc/>
    public async Task SendFileTransferChunkAsync(FileTransferChunk chunk)
    {
        var msg = new NetworkMessage
        {
            MessageType = MsgTypeFileTransferChunk,
            Payload = Encode(chunk)
        };
        await SendMessageAsync(msg);
    }

    /// <inheritdoc/>
    public async Task SendFileTransferCompleteAsync(FileTransferComplete complete)
    {
        var msg = new NetworkMessage
        {
            MessageType = MsgTypeFileTransferComplete,
            Payload = Encode(complete)
        };
        await SendMessageAsync(msg);
    }

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
            var lenBuf = new byte[4];

            while (!ct.IsCancellationRequested)
            {
                // Read length prefix
                int read = await ReadExactAsync(stream, lenBuf, 0, 4, ct);
                if (read < 4)
                {
                    Console.WriteLine("[TCP] Connection closed by remote.");
                    break;
                }

                int length = BitConverter.ToInt32(lenBuf, 0);

                if (length <= 0 || length > 64 * 1024 * 1024) // 64 MB guard
                {
                    Console.WriteLine($"[TCP] Invalid message length {length}; closing.");
                    break;
                }

                var buf = new byte[length];
                read = await ReadExactAsync(stream, buf, 0, length, ct);
                if (read < length)
                {
                    Console.WriteLine("[TCP] Incomplete message; closing.");
                    break;
                }

                DispatchMessage(buf);
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

    private void DispatchMessage(byte[] buf)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<NetworkMessage>(buf);
            if (msg == null) return;

            switch (msg.MessageType)
            {
                case MsgTypeScreen:
                    var sd = Decode<ScreenData>(msg.Payload);
                    if (sd != null) ScreenDataReceived?.Invoke(this, sd);
                    break;

                case MsgTypeInput:
                    var ie = Decode<InputEvent>(msg.Payload);
                    if (ie != null) InputEventReceived?.Invoke(this, ie);
                    break;

                case MsgTypePairingRequest:
                    var pr = Decode<PairingRequest>(msg.Payload);
                    if (pr != null) PairingRequestReceived?.Invoke(this, pr);
                    break;

                case MsgTypePairingResponse:
                    var prr = Decode<PairingResponse>(msg.Payload);
                    if (prr != null) PairingResponseReceived?.Invoke(this, prr);
                    break;

                case MsgTypeConnectionQuality:
                    var cq = Decode<ConnectionQuality>(msg.Payload);
                    if (cq != null) ConnectionQualityReceived?.Invoke(this, cq);
                    break;

                case MsgTypeClipboard:
                    var cd = Decode<ClipboardData>(msg.Payload);
                    if (cd != null) ClipboardDataReceived?.Invoke(this, cd);
                    break;

                case MsgTypeFileTransferRequest:
                    var ftr = Decode<FileTransferRequest>(msg.Payload);
                    if (ftr != null) FileTransferRequestReceived?.Invoke(this, ftr);
                    break;

                case MsgTypeFileTransferResponse:
                    var ftresp = Decode<FileTransferResponse>(msg.Payload);
                    if (ftresp != null) FileTransferResponseReceived?.Invoke(this, ftresp);
                    break;

                case MsgTypeFileTransferChunk:
                    var ftc = Decode<FileTransferChunk>(msg.Payload);
                    if (ftc != null) FileTransferChunkReceived?.Invoke(this, ftc);
                    break;

                case MsgTypeFileTransferComplete:
                    var ftcomplete = Decode<FileTransferComplete>(msg.Payload);
                    if (ftcomplete != null) FileTransferCompleteReceived?.Invoke(this, ftcomplete);
                    break;

                default:
                    Console.WriteLine($"[TCP] Unknown message type: {msg.MessageType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP] Dispatch error: {ex.Message}");
        }
    }

    private async Task SendMessageAsync(NetworkMessage msg)
    {
        var writeStream = (_activeSslStream as Stream) ?? _activeStream;
        
        if (writeStream == null)
        {
            Console.WriteLine("[TCP] SendMessage: not connected.");
            return;
        }

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(msg);
            var lenBuf = BitConverter.GetBytes(json.Length);

            // Lock-free for simplicity; in high-throughput code use a write semaphore
            await writeStream.WriteAsync(lenBuf);
            await writeStream.WriteAsync(json);
            await writeStream.FlushAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP] Send error: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, false);
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

    private static string Encode<T>(T obj)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(obj);
        return Convert.ToBase64String(json);
    }

    private static T? Decode<T>(string payload)
    {
        var json = Convert.FromBase64String(payload);
        return JsonSerializer.Deserialize<T>(json);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}
