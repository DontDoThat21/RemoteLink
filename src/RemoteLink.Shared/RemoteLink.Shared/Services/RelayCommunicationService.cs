using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Relay-backed communication transport used when direct UDP/TCP connectivity is unavailable.
/// </summary>
public sealed class RelayCommunicationService : ICommunicationService, IDisposable
{
    private sealed class NetworkMessage
    {
        public string MessageType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }

    private const string MsgTypeScreen = "ScreenData";
    private const string MsgTypeInput = "InputEvent";
    private const string MsgTypePairingRequest = "PairingRequest";
    private const string MsgTypePairingResponse = "PairingResponse";
    private const string MsgTypeConnectionQuality = "ConnectionQuality";
    private const string MsgTypeSessionControlRequest = "SessionControlRequest";
    private const string MsgTypeSessionControlResponse = "SessionControlResponse";
    private const string MsgTypeClipboard = "ClipboardData";
    private const string MsgTypeFileTransferRequest = "FileTransferRequest";
    private const string MsgTypeFileTransferResponse = "FileTransferResponse";
    private const string MsgTypeFileTransferChunk = "FileTransferChunk";
    private const string MsgTypeFileTransferComplete = "FileTransferComplete";
    private const string MsgTypeAudio = "AudioData";
    private const string MsgTypeChatMessage = "ChatMessage";
    private const string MsgTypeMessageRead = "MessageRead";
    private const string MsgTypePrintJob = "PrintJob";
    private const string MsgTypePrintJobResponse = "PrintJobResponse";
    private const string MsgTypePrintJobStatus = "PrintJobStatus";

    private readonly RelayConfiguration _configuration;
    private readonly SecureTunnelConfiguration _secureTunnelConfiguration;
    private readonly ProxyConfiguration _proxyConfiguration;
    private readonly DeviceInfo _localDevice;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private TcpClient? _relayClient;
    private NetworkStream? _relayStream;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private TaskCompletionSource<bool>? _registerTcs;
    private TaskCompletionSource<RelayFrame>? _connectTcs;
    private string? _connectedRelayHost;
    private int? _connectedRelayPort;
    private string? _sessionId;
    private string? _remoteDeviceId;
    private bool _useSecureTunnel;
    private bool _remoteSupportsSecureTunnel;
    private bool _remoteRequiresSecureTunnel;
    private bool _disposed;

    public RelayCommunicationService(
        DeviceInfo localDevice,
        RelayConfiguration? configuration = null,
        SecureTunnelConfiguration? secureTunnelConfiguration = null,
        ProxyConfiguration? proxyConfiguration = null)
    {
        _localDevice = localDevice ?? throw new ArgumentNullException(nameof(localDevice));
        _configuration = configuration ?? new RelayConfiguration();
        _secureTunnelConfiguration = secureTunnelConfiguration ?? new SecureTunnelConfiguration();
        _proxyConfiguration = proxyConfiguration ?? new ProxyConfiguration();
        _configuration.ApplyTo(_localDevice);
        _secureTunnelConfiguration.ApplyTo(_localDevice);
    }

    public bool IsRelayConfigured => _configuration.IsConfigured;
    public bool IsConnected => !string.IsNullOrWhiteSpace(_sessionId);

    public event EventHandler<ScreenData>? ScreenDataReceived;
    public event EventHandler<InputEvent>? InputEventReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<PairingRequest>? PairingRequestReceived;
    public event EventHandler<PairingResponse>? PairingResponseReceived;
    public event EventHandler<ConnectionQuality>? ConnectionQualityReceived;
    public event EventHandler<SessionControlRequest>? SessionControlRequestReceived;
    public event EventHandler<SessionControlResponse>? SessionControlResponseReceived;
    public event EventHandler<ClipboardData>? ClipboardDataReceived;
    public event EventHandler<FileTransferRequest>? FileTransferRequestReceived;
    public event EventHandler<FileTransferResponse>? FileTransferResponseReceived;
    public event EventHandler<FileTransferChunk>? FileTransferChunkReceived;
    public event EventHandler<FileTransferComplete>? FileTransferCompleteReceived;
    public event EventHandler<AudioData>? AudioDataReceived;
    public event EventHandler<ChatMessage>? ChatMessageReceived;
    public event EventHandler<string>? MessageReadReceived;
    public event EventHandler<PrintJob>? PrintJobReceived;
    public event EventHandler<PrintJobResponse>? PrintJobResponseReceived;
    public event EventHandler<PrintJobStatus>? PrintJobStatusReceived;

    public async Task StartAsync(int port)
    {
        ThrowIfDisposed();
        if (!IsRelayConfigured)
            return;

        _configuration.ApplyTo(_localDevice);
        _secureTunnelConfiguration.ApplyTo(_localDevice);
        await EnsureRelayConnectionAsync();
    }

    public async Task StopAsync()
    {
        await DisconnectAsync();
        await CloseRelayConnectionAsync();
    }

    public async Task<bool> ConnectToDeviceAsync(DeviceInfo device)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(device);

        if (!IsRelayAvailableFor(device))
            return false;

        if (device.RequiresSecureTunnel && !_secureTunnelConfiguration.IsConfigured)
            return false;

        await _connectionLock.WaitAsync();
        try
        {
            await EnsureRelayConnectionAsync(device);
            await DisconnectCoreAsync(sendNotification: false);

            var targetDeviceIdentifier = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId)
                ?? DeviceIdentityManager.NormalizeInternetDeviceId(device.DeviceId)
                ?? device.DeviceId;

            if (string.IsNullOrWhiteSpace(targetDeviceIdentifier))
                return false;

            _connectTcs = new TaskCompletionSource<RelayFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            await SendRelayFrameAsync(new RelayFrame
            {
                MessageType = "Connect",
                SourceDeviceId = _localDevice.DeviceId,
                TargetDeviceId = targetDeviceIdentifier,
                Peer = new RelayPeerInfo
                {
                    DeviceId = _localDevice.DeviceId,
                    InternetDeviceId = _localDevice.InternetDeviceId,
                    DeviceName = _localDevice.DeviceName,
                    SupportsSecureTunnel = _localDevice.SupportsSecureTunnel,
                    RequiresSecureTunnel = _localDevice.RequiresSecureTunnel
                }
            }, _cts?.Token ?? CancellationToken.None);

            using var timeoutCts = new CancellationTokenSource(_configuration.ConnectTimeout);
            RelayFrame response;
            try
            {
                response = await _connectTcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _connectTcs = null;
            }

            return response.Success;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await DisconnectCoreAsync(sendNotification: true);
    }

    public Task SendScreenDataAsync(ScreenData screenData) => SendApplicationMessageAsync(MsgTypeScreen, screenData);
    public Task SendInputEventAsync(InputEvent inputEvent) => SendApplicationMessageAsync(MsgTypeInput, inputEvent);
    public Task SendPairingRequestAsync(PairingRequest request) => SendApplicationMessageAsync(MsgTypePairingRequest, request);
    public Task SendPairingResponseAsync(PairingResponse response) => SendApplicationMessageAsync(MsgTypePairingResponse, response);
    public Task SendConnectionQualityAsync(ConnectionQuality quality) => SendApplicationMessageAsync(MsgTypeConnectionQuality, quality);
    public Task SendSessionControlRequestAsync(SessionControlRequest request) => SendApplicationMessageAsync(MsgTypeSessionControlRequest, request);
    public Task SendSessionControlResponseAsync(SessionControlResponse response) => SendApplicationMessageAsync(MsgTypeSessionControlResponse, response);
    public Task SendClipboardDataAsync(ClipboardData clipboardData) => SendApplicationMessageAsync(MsgTypeClipboard, clipboardData);
    public Task SendFileTransferRequestAsync(FileTransferRequest request) => SendApplicationMessageAsync(MsgTypeFileTransferRequest, request);
    public Task SendFileTransferResponseAsync(FileTransferResponse response) => SendApplicationMessageAsync(MsgTypeFileTransferResponse, response);
    public Task SendFileTransferChunkAsync(FileTransferChunk chunk) => SendApplicationMessageAsync(MsgTypeFileTransferChunk, chunk);
    public Task SendFileTransferCompleteAsync(FileTransferComplete complete) => SendApplicationMessageAsync(MsgTypeFileTransferComplete, complete);
    public Task SendAudioDataAsync(AudioData audioData) => SendApplicationMessageAsync(MsgTypeAudio, audioData);
    public Task SendChatMessageAsync(ChatMessage message) => SendApplicationMessageAsync(MsgTypeChatMessage, message);
    public Task SendMessageReadAsync(string messageId) => SendApplicationMessageAsync(MsgTypeMessageRead, messageId);
    public Task SendPrintJobAsync(PrintJob printJob) => SendApplicationMessageAsync(MsgTypePrintJob, printJob);
    public Task SendPrintJobResponseAsync(PrintJobResponse response) => SendApplicationMessageAsync(MsgTypePrintJobResponse, response);
    public Task SendPrintJobStatusAsync(PrintJobStatus status) => SendApplicationMessageAsync(MsgTypePrintJobStatus, status);

    private async Task SendApplicationMessageAsync<T>(string messageType, T payload)
    {
        if (!IsConnected)
            return;

        var message = new NetworkMessage
        {
            MessageType = messageType,
            Payload = JsonSerializer.Serialize(payload)
        };

        var messagePayload = JsonSerializer.SerializeToUtf8Bytes(message);

        await SendRelayFrameAsync(new RelayFrame
        {
            MessageType = "Payload",
            SessionId = _sessionId,
            SourceDeviceId = _localDevice.DeviceId,
            TargetDeviceId = _remoteDeviceId,
            Payload = EncodeApplicationPayload(messagePayload)
        }, _cts?.Token ?? CancellationToken.None);
    }

    private async Task EnsureRelayConnectionAsync(DeviceInfo? remoteDevice = null)
    {
        var targetHost = remoteDevice?.RelayServerHost;
        var targetPort = remoteDevice?.RelayServerPort;
        var relayHost = !string.IsNullOrWhiteSpace(targetHost)
            ? targetHost!
            : (_configuration.IsConfigured ? _configuration.ServerHost : null);
        var relayPort = targetPort is > 0 ? targetPort.Value : _configuration.ServerPort;

        if (string.IsNullOrWhiteSpace(relayHost))
            throw new InvalidOperationException("No relay server is configured or available for this device.");

        if (_relayClient?.Connected == true &&
            string.Equals(_connectedRelayHost, relayHost, StringComparison.OrdinalIgnoreCase) &&
            _connectedRelayPort == relayPort)
        {
            return;
        }

        await CloseRelayConnectionAsync();

        using var timeoutCts = new CancellationTokenSource(_configuration.ConnectTimeout);
        _relayClient = await ProxyTcpClientFactory.ConnectAsync(relayHost, relayPort, _proxyConfiguration, timeoutCts.Token);
        _cts = new CancellationTokenSource();

        _connectedRelayHost = relayHost;
        _connectedRelayPort = relayPort;
        _relayStream = _relayClient.GetStream();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);

        _registerTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendRelayFrameAsync(new RelayFrame
        {
            MessageType = "Register",
            SourceDeviceId = _localDevice.DeviceId,
            Peer = new RelayPeerInfo
            {
                DeviceId = _localDevice.DeviceId,
                DeviceName = _localDevice.DeviceName,
                SupportsSecureTunnel = _localDevice.SupportsSecureTunnel,
                RequiresSecureTunnel = _localDevice.RequiresSecureTunnel
            }
        }, _cts.Token);

        using var registerTimeout = new CancellationTokenSource(_configuration.ConnectTimeout);
        var registered = await _registerTcs.Task.WaitAsync(registerTimeout.Token);
        _registerTcs = null;

        if (!registered)
            throw new InvalidOperationException("Relay registration was rejected.");
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await ReadRelayFrameAsync(cancellationToken);
                if (frame is null)
                    break;

                DispatchRelayFrame(frame);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            if (!_disposed)
                await DisconnectCoreAsync(sendNotification: false);
        }
    }

    private void DispatchRelayFrame(RelayFrame frame)
    {
        switch (frame.MessageType)
        {
            case "RegisterAck":
                if (frame.Success)
                    _localDevice.InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(frame.Peer?.InternetDeviceId);
                _registerTcs?.TrySetResult(frame.Success);
                return;

            case "ConnectAck":
                if (frame.Success)
                {
                    _sessionId = frame.SessionId;
                    _remoteDeviceId = frame.TargetDeviceId ?? frame.Peer?.DeviceId;
                    UpdateSecureTunnelState(frame.Peer);
                    ConnectionStateChanged?.Invoke(this, true);
                }
                _connectTcs?.TrySetResult(frame);
                return;

            case "IncomingConnection":
                _sessionId = frame.SessionId;
                _remoteDeviceId = frame.SourceDeviceId ?? frame.Peer?.DeviceId;
                UpdateSecureTunnelState(frame.Peer);
                ConnectionStateChanged?.Invoke(this, true);
                return;

            case "Disconnect":
                if (IsConnected)
                {
                    _sessionId = null;
                    _remoteDeviceId = null;
                    ResetSecureTunnelState();
                    ConnectionStateChanged?.Invoke(this, false);
                }
                return;

            case "Payload":
                DispatchApplicationPayload(frame.Payload);
                return;
        }
    }

    private void DispatchApplicationPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        try
        {
            if (!TryDecodeApplicationPayload(payload, out var decodedPayload))
                return;

            var message = JsonSerializer.Deserialize<NetworkMessage>(decodedPayload);
            if (message is null)
                return;

            switch (message.MessageType)
            {
                case MsgTypeScreen:
                    InvokeDecoded(message.Payload, ScreenDataReceived);
                    break;
                case MsgTypeInput:
                    InvokeDecoded(message.Payload, InputEventReceived);
                    break;
                case MsgTypePairingRequest:
                    InvokeDecoded(message.Payload, PairingRequestReceived);
                    break;
                case MsgTypePairingResponse:
                    InvokeDecoded(message.Payload, PairingResponseReceived);
                    break;
                case MsgTypeConnectionQuality:
                    InvokeDecoded(message.Payload, ConnectionQualityReceived);
                    break;
                case MsgTypeSessionControlRequest:
                    InvokeDecoded(message.Payload, SessionControlRequestReceived);
                    break;
                case MsgTypeSessionControlResponse:
                    InvokeDecoded(message.Payload, SessionControlResponseReceived);
                    break;
                case MsgTypeClipboard:
                    InvokeDecoded(message.Payload, ClipboardDataReceived);
                    break;
                case MsgTypeFileTransferRequest:
                    InvokeDecoded(message.Payload, FileTransferRequestReceived);
                    break;
                case MsgTypeFileTransferResponse:
                    InvokeDecoded(message.Payload, FileTransferResponseReceived);
                    break;
                case MsgTypeFileTransferChunk:
                    InvokeDecoded(message.Payload, FileTransferChunkReceived);
                    break;
                case MsgTypeFileTransferComplete:
                    InvokeDecoded(message.Payload, FileTransferCompleteReceived);
                    break;
                case MsgTypeAudio:
                    InvokeDecoded(message.Payload, AudioDataReceived);
                    break;
                case MsgTypeChatMessage:
                    InvokeDecoded(message.Payload, ChatMessageReceived);
                    break;
                case MsgTypeMessageRead:
                    InvokeDecoded(message.Payload, MessageReadReceived);
                    break;
                case MsgTypePrintJob:
                    InvokeDecoded(message.Payload, PrintJobReceived);
                    break;
                case MsgTypePrintJobResponse:
                    InvokeDecoded(message.Payload, PrintJobResponseReceived);
                    break;
                case MsgTypePrintJobStatus:
                    InvokeDecoded(message.Payload, PrintJobStatusReceived);
                    break;
            }
        }
        catch
        {
        }
    }

    private static void InvokeDecoded<T>(string payload, EventHandler<T>? handler)
    {
        if (handler is null || string.IsNullOrWhiteSpace(payload))
            return;

        try
        {
            var decoded = JsonSerializer.Deserialize<T>(payload);
            if (decoded is not null)
                handler.Invoke(null, decoded);
        }
        catch
        {
        }
    }

    private async Task<RelayFrame?> ReadRelayFrameAsync(CancellationToken cancellationToken)
    {
        var stream = _relayStream;
        if (stream is null)
            return null;

        var lengthBuffer = new byte[sizeof(int)];
        var read = await ReadExactlyAsync(stream, lengthBuffer, cancellationToken);
        if (read == 0)
            return null;

        if (read != sizeof(int))
            return null;

        var length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length <= 0)
            return null;

        var payload = new byte[length];
        read = await ReadExactlyAsync(stream, payload, cancellationToken);
        if (read != length)
            return null;

        return JsonSerializer.Deserialize<RelayFrame>(payload);
    }

    private async Task SendRelayFrameAsync(RelayFrame frame, CancellationToken cancellationToken)
    {
        var stream = _relayStream;
        if (stream is null)
            return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(frame);
        var length = BitConverter.GetBytes(payload.Length);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(length, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                return offset;

            offset += read;
        }

        return offset;
    }

    private bool IsRelayAvailableFor(DeviceInfo device)
    {
        if (device.SupportsRelay && !string.IsNullOrWhiteSpace(device.RelayServerHost) && device.RelayServerPort is > 0)
            return true;

        return IsRelayConfigured;
    }

    private async Task DisconnectCoreAsync(bool sendNotification)
    {
        if (!IsConnected)
            return;

        if (sendNotification)
        {
            try
            {
                await SendRelayFrameAsync(new RelayFrame
                {
                    MessageType = "Disconnect",
                    SessionId = _sessionId,
                    SourceDeviceId = _localDevice.DeviceId,
                    TargetDeviceId = _remoteDeviceId
                }, _cts?.Token ?? CancellationToken.None);
            }
            catch
            {
            }
        }

        _sessionId = null;
        _remoteDeviceId = null;
        ResetSecureTunnelState();
        ConnectionStateChanged?.Invoke(this, false);
    }

    private async Task CloseRelayConnectionAsync()
    {
        var cts = _cts;
        _cts = null;

        if (cts is not null)
            cts.Cancel();

        try
        {
            if (_receiveTask is not null)
                await _receiveTask;
        }
        catch
        {
        }

        _receiveTask = null;

        try { _relayStream?.Dispose(); } catch { }
        try { _relayClient?.Dispose(); } catch { }
        _relayStream = null;
        _relayClient = null;
        _connectedRelayHost = null;
        _connectedRelayPort = null;
        ResetSecureTunnelState();
        _registerTcs?.TrySetCanceled();
        _connectTcs?.TrySetCanceled();
        _registerTcs = null;
        _connectTcs = null;
        cts?.Dispose();
    }

    private string EncodeApplicationPayload(byte[] payload)
    {
        if (!_useSecureTunnel)
            return Convert.ToBase64String(payload);

        if (string.IsNullOrWhiteSpace(_sessionId) || string.IsNullOrWhiteSpace(_remoteDeviceId))
            throw new InvalidOperationException("Secure tunnel is not ready.");

        return SecureTunnelPayloadCodec.Encode(
            payload,
            _secureTunnelConfiguration.SharedSecret,
            _sessionId,
            _localDevice.DeviceId,
            _remoteDeviceId);
    }

    private bool TryDecodeApplicationPayload(string payload, out byte[] decodedPayload)
    {
        if (!_useSecureTunnel)
        {
            decodedPayload = Convert.FromBase64String(payload);
            return true;
        }

        if (string.IsNullOrWhiteSpace(_sessionId) || string.IsNullOrWhiteSpace(_remoteDeviceId))
        {
            decodedPayload = Array.Empty<byte>();
            return false;
        }

        return SecureTunnelPayloadCodec.TryDecode(
            payload,
            _secureTunnelConfiguration.SharedSecret,
            _sessionId,
            _localDevice.DeviceId,
            _remoteDeviceId,
            out decodedPayload);
    }

    private void UpdateSecureTunnelState(RelayPeerInfo? peer)
    {
        _remoteSupportsSecureTunnel = peer?.SupportsSecureTunnel == true;
        _remoteRequiresSecureTunnel = peer?.RequiresSecureTunnel == true;
        _useSecureTunnel = _secureTunnelConfiguration.IsConfigured &&
            (_remoteRequiresSecureTunnel ||
             _secureTunnelConfiguration.RequiresTunnel ||
             (_secureTunnelConfiguration.Mode == SecureTunnelMode.Preferred && _remoteSupportsSecureTunnel));
    }

    private void ResetSecureTunnelState()
    {
        _useSecureTunnel = false;
        _remoteSupportsSecureTunnel = false;
        _remoteRequiresSecureTunnel = false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RelayCommunicationService));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _writeLock.Dispose();
        _connectionLock.Dispose();
    }
}
