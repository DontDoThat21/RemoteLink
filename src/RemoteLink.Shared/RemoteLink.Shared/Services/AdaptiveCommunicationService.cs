using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Composite communication service that prefers direct UDP/TCP paths and falls back to relay transport when needed.
/// </summary>
public sealed class AdaptiveCommunicationService : ICommunicationService, IDisposable
{
    private readonly ILogger<AdaptiveCommunicationService> _logger;
    private readonly TcpCommunicationService _tcpService;
    private readonly UdpNatCommunicationService _udpService;
    private readonly RelayCommunicationService? _relayService;
    private ICommunicationService? _activeService;
    private bool _disposed;

    public AdaptiveCommunicationService(
        INatTraversalService natTraversalService,
        DeviceInfo? localDevice = null,
        RelayConfiguration? relayConfiguration = null,
        ILogger<AdaptiveCommunicationService>? logger = null)
    {
        _logger = logger ?? NullLogger<AdaptiveCommunicationService>.Instance;
        _tcpService = new TcpCommunicationService();
        _udpService = new UdpNatCommunicationService(natTraversalService);
        _relayService = localDevice is not null && relayConfiguration?.IsConfigured == true
            ? new RelayCommunicationService(localDevice, relayConfiguration)
            : null;

        WireTransport(_tcpService);
        WireTransport(_udpService);
        if (_relayService is not null)
            WireTransport(_relayService);
    }

    public bool IsConnected => _activeService?.IsConnected == true;

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
        if (_relayService is not null)
            await _relayService.StartAsync(port);
        await _udpService.StartAsync(port);
        await _tcpService.StartAsync(port);
    }

    public async Task StopAsync()
    {
        if (_relayService is not null)
            await _relayService.StopAsync();
        await _udpService.StopAsync();
        await _tcpService.StopAsync();
        _activeService = null;
    }

    public async Task<bool> ConnectToDeviceAsync(DeviceInfo device)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(device);

        var shouldTryUdp = (device.NatCandidates?.Count > 0) || (!string.IsNullOrWhiteSpace(device.PublicIPAddress) && device.PublicPort is > 0);
        if (shouldTryUdp)
        {
            try
            {
                if (await _udpService.ConnectToDeviceAsync(device))
                {
                    _activeService = _udpService;
                    _logger.LogInformation("Connected to {Device} over UDP NAT transport", device.DeviceName);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "UDP NAT transport failed for {Device}; falling back to TCP", device.DeviceName);
            }
        }

        var canTryDirectTcp = !string.IsNullOrWhiteSpace(device.IPAddress) && device.Port is > 0;
        if (canTryDirectTcp)
        {
            try
            {
                var tcpConnected = await _tcpService.ConnectToDeviceAsync(device);
                if (tcpConnected)
                {
                    _activeService = _tcpService;
                    _logger.LogInformation("Connected to {Device} over TCP transport", device.DeviceName);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Direct TCP transport failed for {Device}; falling back to relay if available", device.DeviceName);
            }
        }

        if (_relayService is not null &&
            (device.SupportsRelay || !string.IsNullOrWhiteSpace(device.RelayServerHost) || _relayService.IsRelayConfigured))
        {
            try
            {
                if (await _relayService.ConnectToDeviceAsync(device))
                {
                    _activeService = _relayService;
                    _logger.LogInformation("Connected to {Device} over relay transport", device.DeviceName);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Relay transport failed for {Device}", device.DeviceName);
            }
        }

        return false;
    }

    public async Task DisconnectAsync()
    {
        if (_relayService is not null)
            await _relayService.DisconnectAsync();
        await _udpService.DisconnectAsync();
        await _tcpService.DisconnectAsync();
        _activeService = null;
    }

    public Task SendScreenDataAsync(ScreenData screenData) => ActiveOrDefault().SendScreenDataAsync(screenData);
    public Task SendInputEventAsync(InputEvent inputEvent) => ActiveOrDefault().SendInputEventAsync(inputEvent);
    public Task SendPairingRequestAsync(PairingRequest request) => ActiveOrDefault().SendPairingRequestAsync(request);
    public Task SendPairingResponseAsync(PairingResponse response) => ActiveOrDefault().SendPairingResponseAsync(response);
    public Task SendConnectionQualityAsync(ConnectionQuality quality) => ActiveOrDefault().SendConnectionQualityAsync(quality);
    public Task SendSessionControlRequestAsync(SessionControlRequest request) => ActiveOrDefault().SendSessionControlRequestAsync(request);
    public Task SendSessionControlResponseAsync(SessionControlResponse response) => ActiveOrDefault().SendSessionControlResponseAsync(response);
    public Task SendClipboardDataAsync(ClipboardData clipboardData) => ActiveOrDefault().SendClipboardDataAsync(clipboardData);
    public Task SendFileTransferRequestAsync(FileTransferRequest request) => ActiveOrDefault().SendFileTransferRequestAsync(request);
    public Task SendFileTransferResponseAsync(FileTransferResponse response) => ActiveOrDefault().SendFileTransferResponseAsync(response);
    public Task SendFileTransferChunkAsync(FileTransferChunk chunk) => ActiveOrDefault().SendFileTransferChunkAsync(chunk);
    public Task SendFileTransferCompleteAsync(FileTransferComplete complete) => ActiveOrDefault().SendFileTransferCompleteAsync(complete);
    public Task SendAudioDataAsync(AudioData audioData) => ActiveOrDefault().SendAudioDataAsync(audioData);
    public Task SendChatMessageAsync(ChatMessage message) => ActiveOrDefault().SendChatMessageAsync(message);
    public Task SendMessageReadAsync(string messageId) => ActiveOrDefault().SendMessageReadAsync(messageId);
    public Task SendPrintJobAsync(PrintJob printJob) => ActiveOrDefault().SendPrintJobAsync(printJob);
    public Task SendPrintJobResponseAsync(PrintJobResponse response) => ActiveOrDefault().SendPrintJobResponseAsync(response);
    public Task SendPrintJobStatusAsync(PrintJobStatus status) => ActiveOrDefault().SendPrintJobStatusAsync(status);

    private ICommunicationService ActiveOrDefault()
        => _activeService ?? (_udpService.IsConnected ? _udpService : _relayService?.IsConnected == true ? _relayService : _tcpService);

    private void WireTransport(ICommunicationService transport)
    {
        transport.ConnectionStateChanged += (_, connected) =>
        {
            if (connected)
                _activeService = transport;
            else if (_activeService == transport)
                _activeService = _udpService.IsConnected
                    ? _udpService
                    : _relayService?.IsConnected == true
                        ? _relayService
                        : (_tcpService.IsConnected ? _tcpService : null);

            ConnectionStateChanged?.Invoke(this, _activeService?.IsConnected == true);
        };

        transport.ScreenDataReceived += (_, value) => Relay(transport, ScreenDataReceived, value);
        transport.InputEventReceived += (_, value) => Relay(transport, InputEventReceived, value);
        transport.PairingRequestReceived += (_, value) => Relay(transport, PairingRequestReceived, value);
        transport.PairingResponseReceived += (_, value) => Relay(transport, PairingResponseReceived, value);
        transport.ConnectionQualityReceived += (_, value) => Relay(transport, ConnectionQualityReceived, value);
        transport.SessionControlRequestReceived += (_, value) => Relay(transport, SessionControlRequestReceived, value);
        transport.SessionControlResponseReceived += (_, value) => Relay(transport, SessionControlResponseReceived, value);
        transport.ClipboardDataReceived += (_, value) => Relay(transport, ClipboardDataReceived, value);
        transport.FileTransferRequestReceived += (_, value) => Relay(transport, FileTransferRequestReceived, value);
        transport.FileTransferResponseReceived += (_, value) => Relay(transport, FileTransferResponseReceived, value);
        transport.FileTransferChunkReceived += (_, value) => Relay(transport, FileTransferChunkReceived, value);
        transport.FileTransferCompleteReceived += (_, value) => Relay(transport, FileTransferCompleteReceived, value);
        transport.AudioDataReceived += (_, value) => Relay(transport, AudioDataReceived, value);
        transport.ChatMessageReceived += (_, value) => Relay(transport, ChatMessageReceived, value);
        transport.MessageReadReceived += (_, value) => Relay(transport, MessageReadReceived, value);
        transport.PrintJobReceived += (_, value) => Relay(transport, PrintJobReceived, value);
        transport.PrintJobResponseReceived += (_, value) => Relay(transport, PrintJobResponseReceived, value);
        transport.PrintJobStatusReceived += (_, value) => Relay(transport, PrintJobStatusReceived, value);
    }

    private void Relay<T>(ICommunicationService source, EventHandler<T>? handler, T value)
    {
        if (handler is null)
            return;

        if (_activeService is null)
            _activeService = source;

        if (_activeService == source)
            handler.Invoke(this, value);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AdaptiveCommunicationService));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _relayService?.Dispose();
        _udpService.Dispose();
        _tcpService.Dispose();
    }
}
