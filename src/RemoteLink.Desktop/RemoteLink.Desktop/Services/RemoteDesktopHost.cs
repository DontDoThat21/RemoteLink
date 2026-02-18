using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Main hosted service for the remote desktop host.
/// Responsibilities:
/// <list type="bullet">
/// <item><description>Broadcasts presence on the LAN via UDP discovery.</description></item>
/// <item><description>Listens for incoming TCP connections via <see cref="ICommunicationService"/>.</description></item>
/// <item><description>Streams screen frames to connected clients.</description></item>
/// <item><description>Relays received <see cref="InputEvent"/>s to <see cref="IInputHandler"/>.</description></item>
/// </list>
/// </summary>
public class RemoteDesktopHost : BackgroundService
{
    private readonly ILogger<RemoteDesktopHost> _logger;
    private readonly INetworkDiscovery _networkDiscovery;
    private readonly IScreenCapture _screenCapture;
    private readonly IInputHandler _inputHandler;
    private readonly ICommunicationService _communication;

    // TCP port on which this host listens for client connections.
    private const int HostPort = 12346;

    public RemoteDesktopHost(
        ILogger<RemoteDesktopHost> logger,
        INetworkDiscovery networkDiscovery,
        IScreenCapture screenCapture,
        IInputHandler inputHandler,
        ICommunicationService communication)
    {
        _logger = logger;
        _networkDiscovery = networkDiscovery;
        _screenCapture = screenCapture;
        _inputHandler = inputHandler;
        _communication = communication;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Remote Desktop Host service starting…");

        try
        {
            // Wire input events: when the client sends an input event, relay to input handler
            _communication.InputEventReceived += OnInputEventReceived;

            // Wire connection state: start/stop screen capture based on client presence
            _communication.ConnectionStateChanged += OnConnectionStateChanged;

            // Start TCP listener
            await _communication.StartAsync(HostPort);
            _logger.LogInformation("TCP listener started on port {Port}", HostPort);

            // Start network discovery (UDP broadcast so mobile clients can find us)
            await _networkDiscovery.StartBroadcastingAsync();
            await _networkDiscovery.StartListeningAsync();

            // Start input handler (ready to receive input events from client)
            await _inputHandler.StartAsync();

            _logger.LogInformation("Remote Desktop Host service started. Broadcasting on LAN…");

            // Keep the service alive
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown — not an error
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in Remote Desktop Host service");
            throw;
        }
        finally
        {
            _logger.LogInformation("Remote Desktop Host service stopping…");

            _communication.InputEventReceived -= OnInputEventReceived;
            _communication.ConnectionStateChanged -= OnConnectionStateChanged;

            await _inputHandler.StopAsync();
            await _screenCapture.StopCaptureAsync();
            await _communication.StopAsync();
            await _networkDiscovery.StopBroadcastingAsync();
            await _networkDiscovery.StopListeningAsync();

            _logger.LogInformation("Remote Desktop Host service stopped.");
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the TCP connection state changes.
    /// Starts screen capture on connect; stops it on disconnect to save resources.
    /// </summary>
    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (connected)
        {
            _logger.LogInformation("Client connected — starting screen capture and streaming.");
            _screenCapture.FrameCaptured += OnFrameCaptured;
            _ = _screenCapture.StartCaptureAsync();
        }
        else
        {
            _logger.LogInformation("Client disconnected — stopping screen capture.");
            _screenCapture.FrameCaptured -= OnFrameCaptured;
            _ = _screenCapture.StopCaptureAsync();
        }
    }

    /// <summary>
    /// Called when a new screen frame is ready. Sends it to the connected client.
    /// </summary>
    private void OnFrameCaptured(object? sender, ScreenData screenData)
    {
        if (!_communication.IsConnected) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _communication.SendScreenDataAsync(screenData);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send screen frame to client");
            }
        });
    }

    /// <summary>
    /// Called when the client sends an input event. Relays it to the local input handler.
    /// </summary>
    private void OnInputEventReceived(object? sender, InputEvent inputEvent)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _inputHandler.ProcessInputEventAsync(inputEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process received input event");
            }
        });
    }
}
