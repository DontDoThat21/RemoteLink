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
/// <item><description>Enforces PIN-based pairing via <see cref="IPairingService"/> before streaming begins.</description></item>
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
    private readonly IPairingService _pairing;
    private readonly ISessionManager _sessionManager;

    /// <summary>
    /// Set to true once the currently-connected client has successfully paired.
    /// Reset to false when the client disconnects.
    /// </summary>
    private volatile bool _clientPaired;

    /// <summary>
    /// The active session for the currently connected/paired client, or null when no client is connected.
    /// </summary>
    private RemoteSession? _currentSession;

    // TCP port on which this host listens for client connections.
    private const int HostPort = 12346;

    public RemoteDesktopHost(
        ILogger<RemoteDesktopHost> logger,
        INetworkDiscovery networkDiscovery,
        IScreenCapture screenCapture,
        IInputHandler inputHandler,
        ICommunicationService communication,
        IPairingService pairing,
        ISessionManager sessionManager)
    {
        _logger = logger;
        _networkDiscovery = networkDiscovery;
        _screenCapture = screenCapture;
        _inputHandler = inputHandler;
        _communication = communication;
        _pairing = pairing;
        _sessionManager = sessionManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Remote Desktop Host service starting…");

        try
        {
            // Generate a fresh PIN and display it — the remote user must enter this
            var pin = _pairing.GeneratePin();
            _logger.LogInformation("════════════════════════════════════════");
            _logger.LogInformation("  RemoteLink PIN: {Pin}", pin);
            _logger.LogInformation("  Enter this PIN on your mobile device.");
            _logger.LogInformation("════════════════════════════════════════");

            // Wire pairing: handle pairing request before allowing screen/input
            _communication.PairingRequestReceived += OnPairingRequestReceived;

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

            _communication.PairingRequestReceived -= OnPairingRequestReceived;
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
    /// Called when a client sends a pairing request.
    /// Validates the PIN and either accepts (starts streaming) or rejects the client.
    /// </summary>
    private void OnPairingRequestReceived(object? sender, PairingRequest request)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                bool valid = _pairing.ValidatePin(request.Pin);

                if (valid)
                {
                    _clientPaired = true;

                    // Create and track session
                    _currentSession = _sessionManager.CreateSession(
                        hostId: Environment.MachineName,
                        hostDeviceName: Environment.MachineName,
                        clientId: request.ClientDeviceId,
                        clientDeviceName: request.ClientDeviceName);

                    var token = _currentSession.SessionId;

                    // Transition session to Connected state
                    _sessionManager.OnConnected(_currentSession.SessionId);

                    var response = new PairingResponse
                    {
                        Success = true,
                        SessionToken = token,
                        Message = "Pairing accepted."
                    };
                    await _communication.SendPairingResponseAsync(response);

                    _logger.LogInformation(
                        "Client '{Name}' ({Id}) paired successfully. Session {SessionId} created.",
                        request.ClientDeviceName,
                        request.ClientDeviceId,
                        _currentSession.SessionId[..8]);

                    // Client is now trusted — start screen capture and streaming
                    _screenCapture.FrameCaptured += OnFrameCaptured;
                    _ = _screenCapture.StartCaptureAsync();
                }
                else
                {
                    var reason = _pairing.IsLockedOut
                        ? PairingFailureReason.TooManyAttempts
                        : PairingFailureReason.InvalidPin;

                    var response = new PairingResponse
                    {
                        Success = false,
                        FailureReason = reason,
                        Message = reason == PairingFailureReason.TooManyAttempts
                            ? "Too many failed attempts. Please ask the host to refresh the PIN."
                            : "Invalid PIN. Please check the PIN displayed on the host."
                    };
                    await _communication.SendPairingResponseAsync(response);

                    _logger.LogWarning(
                        "Pairing rejected for client '{Name}' ({Id}): {Reason}",
                        request.ClientDeviceName,
                        request.ClientDeviceId,
                        reason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing pairing request");
            }
        });
    }

    /// <summary>
    /// Called when the TCP connection state changes.
    /// On disconnect: resets paired state and stops screen capture.
    /// Note: screen capture is now started from <see cref="OnPairingRequestReceived"/>
    /// after successful pairing, not directly on connect.
    /// </summary>
    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (connected)
        {
            _logger.LogInformation("Client connected — awaiting PIN pairing before streaming.");
            // Do NOT start screen capture yet; wait for successful pairing.
        }
        else
        {
            _clientPaired = false;
            _logger.LogInformation("Client disconnected — stopping screen capture.");
            _screenCapture.FrameCaptured -= OnFrameCaptured;
            _ = _screenCapture.StopCaptureAsync();

            // End the current session if one exists
            if (_currentSession != null)
            {
                _sessionManager.EndSession(_currentSession.SessionId);
                _logger.LogInformation(
                    "Session {SessionId} ended. Duration: {Duration:mm\\:ss}",
                    _currentSession.SessionId[..8],
                    _currentSession.Duration);
                _currentSession = null;
            }

            // Refresh the PIN ready for the next connection attempt
            var newPin = _pairing.GeneratePin();
            _logger.LogInformation(
                "New PIN generated for next connection: {Pin}", newPin);
        }
    }

    /// <summary>
    /// Called when a new screen frame is ready. Sends it to the connected client.
    /// Only fires when a client has successfully paired.
    /// </summary>
    private void OnFrameCaptured(object? sender, ScreenData screenData)
    {
        if (!_communication.IsConnected || !_clientPaired) return;

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
    /// Ignored if the client has not yet successfully paired.
    /// </summary>
    private void OnInputEventReceived(object? sender, InputEvent inputEvent)
    {
        if (!_clientPaired) return;

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
