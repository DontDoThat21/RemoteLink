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
    private readonly IDeltaFrameEncoder _deltaEncoder;
    private readonly IPerformanceMonitor _perfMonitor;
    private readonly IClipboardService _clipboardService;
    private readonly IAudioCaptureService _audioCapture;
    private readonly ISessionRecorder _sessionRecorder;

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
        ISessionManager sessionManager,
        IDeltaFrameEncoder deltaEncoder,
        IPerformanceMonitor perfMonitor,
        IClipboardService clipboardService,
        IAudioCaptureService audioCapture,
        ISessionRecorder sessionRecorder)
    {
        _logger = logger;
        _networkDiscovery = networkDiscovery;
        _screenCapture = screenCapture;
        _inputHandler = inputHandler;
        _communication = communication;
        _pairing = pairing;
        _sessionManager = sessionManager;
        _deltaEncoder = deltaEncoder;
        _perfMonitor = perfMonitor;
        _clipboardService = clipboardService;
        _audioCapture = audioCapture;
        _sessionRecorder = sessionRecorder;
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

            // Wire clipboard sync: send local changes to client, apply remote changes locally
            _clipboardService.ClipboardChanged += OnClipboardChanged;
            _communication.ClipboardDataReceived += OnClipboardDataReceived;

            // Wire audio streaming: when audio is captured, send to client
            _audioCapture.AudioCaptured += OnAudioCaptured;

            // Start TCP listener
            await _communication.StartAsync(HostPort);
            _logger.LogInformation("TCP listener started on port {Port}", HostPort);

            // Start network discovery (UDP broadcast so mobile clients can find us)
            await _networkDiscovery.StartBroadcastingAsync();
            await _networkDiscovery.StartListeningAsync();

            // Start input handler (ready to receive input events from client)
            await _inputHandler.StartAsync();

            _logger.LogInformation("Remote Desktop Host service started. Broadcasting on LAN…");

            // Keep the service alive and send periodic quality updates
            var lastQualityUpdate = DateTime.MinValue;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);

                // Send connection quality updates every 2 seconds when client is paired
                if (_clientPaired && (DateTime.UtcNow - lastQualityUpdate).TotalSeconds >= 2)
                {
                    await SendConnectionQualityUpdateAsync();
                    lastQualityUpdate = DateTime.UtcNow;
                }
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
            _clipboardService.ClipboardChanged -= OnClipboardChanged;
            _communication.ClipboardDataReceived -= OnClipboardDataReceived;
            _audioCapture.AudioCaptured -= OnAudioCaptured;

            await _audioCapture.StopAsync();
            await _clipboardService.StopAsync();
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

                    // Start clipboard monitoring for bidirectional sync
                    _ = _clipboardService.StartAsync();

                    // Start audio capture and streaming
                    _ = _audioCapture.StartAsync();

                    // Start session recording (creates recordings/ directory if needed)
                    var recordingPath = Path.Combine("recordings", 
                        $"session_{_currentSession.SessionId[..8]}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
                    var recordingStarted = await _sessionRecorder.StartRecordingAsync(recordingPath, frameRate: 15, includeAudio: true);
                    if (recordingStarted)
                        _logger.LogInformation("Session recording started: {Path}", recordingPath);
                    else
                        _logger.LogWarning("Failed to start session recording");
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
            _logger.LogInformation("Client disconnected — stopping screen capture, audio, clipboard sync, and recording.");
            _screenCapture.FrameCaptured -= OnFrameCaptured;
            _ = _screenCapture.StopCaptureAsync();
            _ = _audioCapture.StopAsync();
            _ = _clipboardService.StopAsync();

            // Stop session recording
            if (_sessionRecorder.IsRecording)
            {
                _ = _sessionRecorder.StopRecordingAsync();
                _logger.LogInformation("Session recording stopped. Duration: {Duration:mm\\:ss}", 
                    _sessionRecorder.RecordedDuration);
            }

            // Reset performance optimization state
            _deltaEncoder.Reset();
            _perfMonitor.Reset();

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
    /// Applies delta encoding and adaptive quality based on connection performance.
    /// </summary>
    private void OnFrameCaptured(object? sender, ScreenData screenData)
    {
        if (!_communication.IsConnected || !_clientPaired) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var sendStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // Apply adaptive quality
                int recommendedQuality = _perfMonitor.GetRecommendedQuality();
                _screenCapture.SetQuality(recommendedQuality);

                // Apply delta encoding
                var (encodedFrame, isDelta) = await _deltaEncoder.EncodeFrameAsync(screenData);

                // Send the optimized frame
                await _communication.SendScreenDataAsync(encodedFrame);

                // Write frame to session recording (if recording is active)
                await _sessionRecorder.WriteFrameAsync(encodedFrame);

                // Record performance metrics
                long sendEndTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long latency = sendEndTime - sendStartTime;
                _perfMonitor.RecordFrameSent(encodedFrame.ImageData.Length, latency);

                if (isDelta)
                {
                    _logger.LogTrace(
                        "Sent delta frame {FrameId} ({Size} bytes, {Regions} regions, quality {Quality}, latency {Latency}ms)",
                        encodedFrame.FrameId[..8],
                        encodedFrame.ImageData.Length,
                        encodedFrame.DeltaRegions?.Count ?? 0,
                        recommendedQuality,
                        latency);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send screen frame to client");
            }
        });
    }

    /// <summary>
    /// Called when audio data is captured from the system.
    /// Sends audio to the connected client when paired.
    /// </summary>
    private void OnAudioCaptured(object? sender, AudioData audioData)
    {
        if (!_communication.IsConnected || !_clientPaired) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _communication.SendAudioDataAsync(audioData);

                // Write audio to session recording (if recording is active)
                await _sessionRecorder.WriteAudioAsync(audioData);

                _logger.LogTrace(
                    "Sent audio chunk: {Size} bytes, {Duration}ms, {SampleRate}Hz, {Channels}ch",
                    audioData.Data.Length,
                    audioData.DurationMs,
                    audioData.SampleRate,
                    audioData.Channels);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send audio data to client");
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

    /// <summary>
    /// Called when local clipboard content changes.
    /// Sends clipboard data to the paired client for synchronization.
    /// </summary>
    private void OnClipboardChanged(object? sender, ClipboardChangedEventArgs e)
    {
        if (!_communication.IsConnected || !_clientPaired) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var clipboardData = new ClipboardData
                {
                    ContentType = (ClipboardContentType)e.ContentType,
                    Text = e.Text,
                    ImageData = e.ImageData,
                    Timestamp = DateTime.UtcNow
                };

                await _communication.SendClipboardDataAsync(clipboardData);

                _logger.LogDebug(
                    "Sent clipboard data to client: {Type} ({Size} bytes)",
                    clipboardData.ContentType,
                    clipboardData.Text?.Length ?? clipboardData.ImageData?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send clipboard data to client");
            }
        });
    }

    /// <summary>
    /// Called when clipboard data is received from the client.
    /// Applies the changes to the local clipboard.
    /// </summary>
    private void OnClipboardDataReceived(object? sender, ClipboardData clipboardData)
    {
        if (!_clientPaired) return;

        _ = Task.Run(async () =>
        {
            try
            {
                switch (clipboardData.ContentType)
                {
                    case ClipboardContentType.Text when !string.IsNullOrEmpty(clipboardData.Text):
                        await _clipboardService.SetTextAsync(clipboardData.Text);
                        _logger.LogDebug("Applied clipboard text from client: {Length} chars", clipboardData.Text.Length);
                        break;

                    case ClipboardContentType.Image when clipboardData.ImageData != null:
                        await _clipboardService.SetImageAsync(clipboardData.ImageData);
                        _logger.LogDebug("Applied clipboard image from client: {Size} bytes", clipboardData.ImageData.Length);
                        break;

                    default:
                        _logger.LogDebug("Received clipboard data with no content");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply clipboard data from client");
            }
        });
    }

    /// <summary>
    /// Sends connection quality metrics to the connected client.
    /// Called periodically (every 2 seconds) when a client is paired.
    /// </summary>
    private async Task SendConnectionQualityUpdateAsync()
    {
        try
        {
            var quality = new ConnectionQuality
            {
                Fps = _perfMonitor.GetCurrentFps(),
                Bandwidth = _perfMonitor.GetCurrentBandwidth(),
                Latency = _perfMonitor.GetAverageLatency(),
                Timestamp = DateTime.UtcNow
            };

            // Calculate overall rating
            quality.Rating = ConnectionQuality.CalculateRating(
                quality.Fps, quality.Latency, quality.Bandwidth);

            await _communication.SendConnectionQualityAsync(quality);

            _logger.LogDebug(
                "Connection quality: {Rating} (FPS: {Fps:F1}, Bandwidth: {Bandwidth}, Latency: {Latency}ms)",
                quality.Rating,
                quality.Fps,
                quality.GetBandwidthString(),
                quality.Latency);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send connection quality update");
        }
    }
}
