using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

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

    // ── State ────────────────────────────────────────────────────────────────

    private TcpListener? _listener;
    private TcpClient? _activeClient;
    private NetworkStream? _activeStream;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _acceptTask;
    private bool _disposed;

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
                ConnectionStateChanged?.Invoke(this, true);

                _receiveTask = ReceiveLoopAsync(_activeStream, ct);
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
            ConnectionStateChanged?.Invoke(this, true);

            Console.WriteLine($"[TCP] Connected to {device.DeviceName} at {device.IPAddress}:{device.Port}");

            _receiveTask = ReceiveLoopAsync(_activeStream, _cts.Token);
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

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken ct)
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
        if (_activeStream == null)
        {
            Console.WriteLine("[TCP] SendMessage: not connected.");
            return;
        }

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(msg);
            var lenBuf = BitConverter.GetBytes(json.Length);

            // Lock-free for simplicity; in high-throughput code use a write semaphore
            await _activeStream.WriteAsync(lenBuf);
            await _activeStream.WriteAsync(json);
            await _activeStream.FlushAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP] Send error: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, false);
        }
    }

    private async Task DropConnectionAsync()
    {
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

    private static async Task<int> ReadExactAsync(
        NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
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
