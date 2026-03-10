using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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
    private readonly IMessagingService _messagingService;
    private readonly IConnectionRequestNotificationPublisher? _connectionRequestNotificationPublisher;
    private readonly INatTraversalService? _natTraversalService;
    private readonly ISignalingService? _signalingService;
    private readonly DeviceInfo? _localDevice;
    private readonly IUserAccountService? _userAccountService;
    private readonly object _auditLock = new();

    /// <summary>
    /// Set to true once the currently-connected client has successfully paired.
    /// Reset to false when the client disconnects.
    /// </summary>
    private volatile bool _clientPaired;
    private SessionPermissionSet _currentPermissions = SessionPermissionSet.CreateFullAccess();
    private ConnectionAuditLogEntry? _currentAuditLogEntry;

    /// <summary>
    /// The active session for the currently connected/paired client, or null when no client is connected.
    /// </summary>
    private RemoteSession? _currentSession;

    private volatile ScreenDataFormat _preferredImageFormat = ScreenDataFormat.Raw;
    private volatile bool _audioStreamingEnabled = true;
    private long _lastFrameSentAtUtcMs;

    // TCP port on which this host listens for client connections.
    private const int HostPort = 12346;
    private const int MaxAdaptiveStreamFps = 10;
    private const int ModerateAdaptiveStreamFps = 6;
    private const int LowBandwidthAdaptiveStreamFps = 4;
    private const int CriticalAdaptiveStreamFps = 2;
    private const int MinimumAdaptiveQuality = 45;
    private const long ModerateLatencyThresholdMs = 75;
    private const long HighLatencyThresholdMs = 150;
    private const long CriticalLatencyThresholdMs = 250;
    private const long ModerateBandwidthThresholdBytesPerSec = 2_500_000;
    private const long LowBandwidthThresholdBytesPerSec = 1_500_000;
    private const long CriticalBandwidthThresholdBytesPerSec = 750_000;

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
        ISessionRecorder sessionRecorder,
        IMessagingService messagingService,
        IConnectionRequestNotificationPublisher? connectionRequestNotificationPublisher = null,
        INatTraversalService? natTraversalService = null,
        ISignalingService? signalingService = null,
        DeviceInfo? localDevice = null,
        IUserAccountService? userAccountService = null)
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
        _messagingService = messagingService;
        _connectionRequestNotificationPublisher = connectionRequestNotificationPublisher;
        _natTraversalService = natTraversalService;
        _signalingService = signalingService;
        _localDevice = localDevice;
        _userAccountService = userAccountService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Remote Desktop Host service starting…");

        try
        {
            if (_userAccountService is not null)
            {
                try
                {
                    await _userAccountService.LoadAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load persisted user account state. Trusted-device checks will be unavailable.");
                }
            }

            // Initialize messaging service with host device info
            _messagingService.Initialize(Environment.MachineName, $"{Environment.MachineName} Host");

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

            // Wire mobile session toolbar controls (monitor selection / quality changes)
            _communication.SessionControlRequestReceived += OnSessionControlRequestReceived;

            // Wire clipboard sync: send local changes to client, apply remote changes locally
            _clipboardService.ClipboardChanged += OnClipboardChanged;
            _communication.ClipboardDataReceived += OnClipboardDataReceived;

            // Wire audio streaming: when audio is captured, send to client
            _audioCapture.AudioCaptured += OnAudioCaptured;

            // Wire messaging: log received messages for debugging
            _messagingService.MessageReceived += OnMessageReceived;

            // Start TCP listener
            await _communication.StartAsync(HostPort);
            _logger.LogInformation("TCP listener started on port {Port}", HostPort);

            if (_natTraversalService is not null && _localDevice is not null)
            {
                try
                {
                    var natDiscovery = await _natTraversalService.StartAsync(HostPort, stoppingToken);
                    ApplyNatDiscoveryResult(_localDevice, natDiscovery);
                    if (_signalingService is not null)
                        await _signalingService.RegisterDeviceAsync(_localDevice, stoppingToken);
                    _logger.LogInformation(
                        "NAT traversal ready. Type={NatType}, public endpoint={PublicIPAddress}:{PublicPort}, candidates={CandidateCount}",
                        natDiscovery.NatType,
                        natDiscovery.PublicIPAddress ?? "unknown",
                        natDiscovery.PublicPort?.ToString() ?? "-",
                        natDiscovery.Candidates.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize NAT traversal. Continuing with LAN-only connectivity.");
                }
            }

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
            _communication.SessionControlRequestReceived -= OnSessionControlRequestReceived;
            _clipboardService.ClipboardChanged -= OnClipboardChanged;
            _communication.ClipboardDataReceived -= OnClipboardDataReceived;
            _audioCapture.AudioCaptured -= OnAudioCaptured;
            _messagingService.MessageReceived -= OnMessageReceived;

            await FinalizeCurrentAuditLogEntryAsync("Host service stopped.");

            await _audioCapture.StopAsync();
            await _clipboardService.StopAsync();
            await _inputHandler.StopAsync();
            await _screenCapture.StopCaptureAsync();
            if (_natTraversalService is not null)
                await _natTraversalService.StopAsync();
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
                await PublishIncomingConnectionRequestAlertAsync(request);

                var blockedDevice = await IsBlockedDeviceAsync(request);
                if (blockedDevice)
                {
                    await _communication.SendPairingResponseAsync(new PairingResponse
                    {
                        Success = false,
                        FailureReason = PairingFailureReason.HostRefused,
                        Message = "Connection refused. This device is blocked by the host."
                    });

                    _logger.LogWarning(
                        "Pairing rejected for blocked client '{Name}' ({Id})",
                        request.ClientDeviceName,
                        request.ClientDeviceId);

                    await PersistRejectedAuditAsync(
                        request,
                        ConnectionAuditOutcome.RejectedBlocked,
                        "Connection rejected because the client device is blocked.");
                    return;
                }

                var trustedDevice = await IsTrustedDeviceAsync(request);
                var sessionPermissions = await ResolveSessionPermissionsAsync(request);
                bool valid = trustedDevice || _pairing.ValidatePin(request.Pin);

                if (valid)
                {
                    _clientPaired = true;
                    _currentPermissions = sessionPermissions;

                    await RegisterManagedClientDeviceAsync(request);

                    // Create and track session
                    _currentSession = _sessionManager.CreateSession(
                        hostId: Environment.MachineName,
                        hostDeviceName: Environment.MachineName,
                        clientId: request.ClientDeviceId,
                        clientDeviceName: request.ClientDeviceName);

                    var token = _currentSession.SessionId;
                    _currentSession.Permissions = _currentPermissions.Clone();
                    InitializeCurrentAuditLogEntry(request, trustedDevice, _currentPermissions, token);

                    // Transition session to Connected state
                    _sessionManager.OnConnected(_currentSession.SessionId);

                    var response = new PairingResponse
                    {
                        Success = true,
                        SessionToken = token,
                        SessionPermissions = _currentPermissions.Clone(),
                        Message = trustedDevice
                            ? "Trusted device accepted without PIN."
                            : "Pairing accepted."
                    };
                    await _communication.SendPairingResponseAsync(response);
                    AppendAuditAction(
                        ConnectionAuditActionType.PairingAccepted,
                        trustedDevice ? "Trusted device paired without PIN." : "Client paired with valid PIN.");
                    await PersistCurrentAuditLogEntryAsync();

                    _logger.LogInformation(
                        trustedDevice
                            ? "Trusted client '{Name}' ({Id}) paired without PIN. Session {SessionId} created."
                            : "Client '{Name}' ({Id}) paired successfully. Session {SessionId} created.",
                        request.ClientDeviceName,
                        request.ClientDeviceId,
                        _currentSession.SessionId[..8]);

                    // Client is now trusted — start screen capture and streaming
                    _screenCapture.FrameCaptured += OnFrameCaptured;
                    _ = _screenCapture.StartCaptureAsync();

                    // Start clipboard monitoring for bidirectional sync
                    if (_currentPermissions.AllowClipboardSync)
                        _ = _clipboardService.StartAsync();

                    // Start audio capture and streaming when enabled for the session
                    if (_currentPermissions.AllowAudioStreaming && _audioStreamingEnabled)
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

                    await PersistRejectedAuditAsync(
                        request,
                        ConnectionAuditOutcome.RejectedInvalidPin,
                        reason == PairingFailureReason.TooManyAttempts
                            ? "Pairing rejected after too many failed PIN attempts."
                            : "Pairing rejected because the supplied PIN was invalid.");
                }
            }

    private async Task<SessionPermissionSet> ResolveSessionPermissionsAsync(PairingRequest request)
    {
        if (_userAccountService is null || !_userAccountService.IsSignedIn)
            return SessionPermissionSet.CreateFullAccess();

        try
        {
            return await _userAccountService.GetDeviceSessionPermissionsAsync(request.ClientDeviceId, request.ClientInternetDeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve session permissions for client '{ClientId}'", request.ClientDeviceId);
            return SessionPermissionSet.CreateFullAccess();
        }
    }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing pairing request");
            }
        });
    }

    private async Task<bool> IsBlockedDeviceAsync(PairingRequest request)
    {
        if (_userAccountService is null || !_userAccountService.IsSignedIn)
            return false;

        try
        {
            return await _userAccountService.IsDeviceBlockedAsync(request.ClientDeviceId, request.ClientInternetDeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate blocked-device status for client '{ClientId}'", request.ClientDeviceId);
            return false;
        }
    }

    private async Task<bool> IsTrustedDeviceAsync(PairingRequest request)
    {
        if (_userAccountService is null || !_userAccountService.IsSignedIn)
            return false;

        try
        {
            return await _userAccountService.IsDeviceTrustedAsync(request.ClientDeviceId, request.ClientInternetDeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate trusted-device status for client '{ClientId}'", request.ClientDeviceId);
            return false;
        }
    }

    private async Task RegisterManagedClientDeviceAsync(PairingRequest request)
    {
        if (_userAccountService is null || !_userAccountService.IsSignedIn)
            return;

        try
        {
            await _userAccountService.RegisterDeviceAsync(new DeviceInfo
            {
                DeviceId = request.ClientDeviceId,
                InternetDeviceId = request.ClientInternetDeviceId,
                DeviceName = string.IsNullOrWhiteSpace(request.ClientDeviceName) ? request.ClientDeviceId : request.ClientDeviceName,
                Type = DeviceType.Unknown,
                IPAddress = string.Empty,
                Port = 0,
                IsOnline = true,
                LastSeen = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to register paired client '{ClientId}' with the signed-in account", request.ClientDeviceId);
        }
    }

    private async Task PublishIncomingConnectionRequestAlertAsync(PairingRequest request)
    {
        if (_connectionRequestNotificationPublisher is null)
            return;

        try
        {
            await _connectionRequestNotificationPublisher.PublishAsync(new IncomingConnectionRequestAlert
            {
                HostDeviceName = Environment.MachineName,
                ClientDeviceId = request.ClientDeviceId,
                ClientDeviceName = request.ClientDeviceName,
                RequestedAt = request.RequestedAt == default ? DateTime.UtcNow : request.RequestedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to publish incoming connection notification");
        }
    }

    private static void ApplyNatDiscoveryResult(DeviceInfo device, NatDiscoveryResult result)
    {
        device.PublicIPAddress = result.PublicIPAddress;
        device.PublicPort = result.PublicPort;
        device.NatType = result.NatType;
        device.NatCandidates = result.Candidates
            .Select(candidate => new NatEndpointCandidate
            {
                CandidateId = candidate.CandidateId,
                IPAddress = candidate.IPAddress,
                Port = candidate.Port,
                Protocol = candidate.Protocol,
                Type = candidate.Type,
                Priority = candidate.Priority,
                Source = candidate.Source
            })
            .ToList();
    }

    private void InitializeCurrentAuditLogEntry(PairingRequest request, bool usedTrustedDevice, SessionPermissionSet permissions, string sessionId)
    {
        var requestedAtUtc = request.RequestedAt == default ? DateTime.UtcNow : request.RequestedAt.ToUniversalTime();
        var clientName = string.IsNullOrWhiteSpace(request.ClientDeviceName) ? request.ClientDeviceId : request.ClientDeviceName;

        lock (_auditLock)
        {
            _currentAuditLogEntry = new ConnectionAuditLogEntry
            {
                SessionId = sessionId,
                ClientDeviceId = request.ClientDeviceId,
                ClientInternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(request.ClientInternetDeviceId),
                ClientDeviceName = clientName,
                RequestedAtUtc = requestedAtUtc,
                ConnectedAtUtc = DateTime.UtcNow,
                Outcome = ConnectionAuditOutcome.Connected,
                UsedTrustedDevice = usedTrustedDevice,
                Permissions = permissions.Clone()
            };
        }
    }

    private void AppendAuditAction(ConnectionAuditActionType actionType, string description)
    {
        lock (_auditLock)
        {
            _currentAuditLogEntry?.Actions.Add(new ConnectionAuditActionEntry
            {
                TimestampUtc = DateTime.UtcNow,
                ActionType = actionType,
                Description = description
            });
        }
    }

    private async Task PersistRejectedAuditAsync(PairingRequest request, ConnectionAuditOutcome outcome, string description)
    {
        if (_userAccountService is null || !_userAccountService.IsSignedIn)
            return;

        var requestedAtUtc = request.RequestedAt == default ? DateTime.UtcNow : request.RequestedAt.ToUniversalTime();
        var clientName = string.IsNullOrWhiteSpace(request.ClientDeviceName) ? request.ClientDeviceId : request.ClientDeviceName;
        var entry = new ConnectionAuditLogEntry
        {
            ClientDeviceId = request.ClientDeviceId,
            ClientInternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(request.ClientInternetDeviceId),
            ClientDeviceName = clientName,
            RequestedAtUtc = requestedAtUtc,
            Outcome = outcome,
            Permissions = SessionPermissionSet.CreateFullAccess(),
            Actions = new List<ConnectionAuditActionEntry>
            {
                new()
                {
                    TimestampUtc = DateTime.UtcNow,
                    ActionType = ConnectionAuditActionType.PairingRejected,
                    Description = description
                }
            }
        };

        try
        {
            await _userAccountService.UpsertConnectionAuditLogEntryAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist rejected connection audit entry for client '{ClientId}'", request.ClientDeviceId);
        }
    }

    private async Task PersistCurrentAuditLogEntryAsync()
    {
        if (_userAccountService is null || !_userAccountService.IsSignedIn)
            return;

        ConnectionAuditLogEntry? snapshot;
        lock (_auditLock)
        {
            snapshot = _currentAuditLogEntry?.Clone();
        }

        if (snapshot is null)
            return;

        try
        {
            await _userAccountService.UpsertConnectionAuditLogEntryAsync(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist connection audit entry for session {SessionId}", snapshot.SessionId ?? snapshot.AuditId);
        }
    }

    private async Task FinalizeCurrentAuditLogEntryAsync(string description)
    {
        if (_userAccountService is null || !_userAccountService.IsSignedIn)
        {
            lock (_auditLock)
            {
                _currentAuditLogEntry = null;
            }

            return;
        }

        ConnectionAuditLogEntry? snapshot;
        lock (_auditLock)
        {
            if (_currentAuditLogEntry is null)
                return;

            _currentAuditLogEntry.DisconnectedAtUtc = DateTime.UtcNow;
            _currentAuditLogEntry.Duration = _currentSession?.Duration ?? _currentAuditLogEntry.Duration;
            _currentAuditLogEntry.Outcome = ConnectionAuditOutcome.Disconnected;
            _currentAuditLogEntry.Actions.Add(new ConnectionAuditActionEntry
            {
                TimestampUtc = DateTime.UtcNow,
                ActionType = ConnectionAuditActionType.SessionDisconnected,
                Description = description
            });
            snapshot = _currentAuditLogEntry.Clone();
            _currentAuditLogEntry = null;
        }

        try
        {
            await _userAccountService.UpsertConnectionAuditLogEntryAsync(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to finalize connection audit entry for session {SessionId}", snapshot.SessionId ?? snapshot.AuditId);
        }
    }

    /// <summary>
    /// Handles monitor and quality control requests from the paired client.
    /// </summary>
    private void OnSessionControlRequestReceived(object? sender, SessionControlRequest request)
    {
        _ = Task.Run(async () =>
        {
            if (!_clientPaired)
            {
                await _communication.SendSessionControlResponseAsync(new SessionControlResponse
                {
                    RequestId = request.RequestId,
                    Command = request.Command,
                    Success = false,
                    ErrorMessage = "Client is not paired."
                });
                return;
            }

            if (!IsSessionControlAllowed(request.Command))
            {
                AppendAuditAction(
                    ConnectionAuditActionType.SessionControlDenied,
                    $"Session control denied: {request.Command}.");
                await _communication.SendSessionControlResponseAsync(new SessionControlResponse
                {
                    RequestId = request.RequestId,
                    Command = request.Command,
                    Success = false,
                    ErrorMessage = "This action is not allowed for the current session."
                });
                return;
            }

            try
            {
                AppendAuditAction(
                    ConnectionAuditActionType.SessionControlRequested,
                    $"Session control requested: {request.Command}.");

                switch (request.Command)
                {
                    case SessionControlCommand.GetMonitors:
                    {
                        var monitors = await _screenCapture.GetMonitorsAsync();
                        await _communication.SendSessionControlResponseAsync(new SessionControlResponse
                        {
                            RequestId = request.RequestId,
                            Command = request.Command,
                            Success = true,
                            Monitors = monitors.ToList(),
                            SelectedMonitorId = _screenCapture.GetSelectedMonitorId()
                        });
                        AppendAuditAction(ConnectionAuditActionType.SessionControlApplied, "Returned remote monitor list.");
                        break;
                    }

                    case SessionControlCommand.SelectMonitor:
                    {
                        if (string.IsNullOrWhiteSpace(request.MonitorId))
                        {
                            await _communication.SendSessionControlResponseAsync(new SessionControlResponse
                            {
                                RequestId = request.RequestId,
                                Command = request.Command,
                                Success = false,
                                ErrorMessage = "Monitor ID is required."
                            });
                            break;
                        }

                        bool selected = await _screenCapture.SelectMonitorAsync(request.MonitorId);
                        if (selected)
                            _deltaEncoder.Reset();

                        await _communication.SendSessionControlResponseAsync(new SessionControlResponse
                        {
                            RequestId = request.RequestId,
                            Command = request.Command,
                            Success = selected,
                            ErrorMessage = selected ? null : "Monitor not found.",
                            SelectedMonitorId = selected ? request.MonitorId : _screenCapture.GetSelectedMonitorId()
                        });
                        AppendAuditAction(
                            selected ? ConnectionAuditActionType.SessionControlApplied : ConnectionAuditActionType.SessionControlDenied,
                            selected
                                ? $"Selected monitor '{request.MonitorId}'."
                                : $"Monitor switch failed for '{request.MonitorId}'.");
                        break;
                    }

                    case SessionControlCommand.SetQuality:
                    {
                        int quality = Math.Clamp(request.Quality ?? 75, 0, 100);
                        _screenCapture.SetQuality(quality);

                        await _communication.SendSessionControlResponseAsync(new SessionControlResponse
                        {
                            RequestId = request.RequestId,
                            Command = request.Command,
                            Success = true,
                            AppliedQuality = quality
                        });
                        AppendAuditAction(ConnectionAuditActionType.SessionControlApplied, $"Set stream quality to {quality}.");
                        break;
                    }

                    case SessionControlCommand.SetImageFormat:
                    {
                        var format = request.ImageFormat ?? ScreenDataFormat.Raw;
                        _preferredImageFormat = format;
                        _deltaEncoder.Reset();

                        await _communication.SendSessionControlResponseAsync(new SessionControlResponse
                        {
                            RequestId = request.RequestId,
                            Command = request.Command,
                            Success = true,
                            AppliedImageFormat = format
                        });
                        AppendAuditAction(ConnectionAuditActionType.SessionControlApplied, $"Set image format to {format}.");
                        break;
                    }

                    case SessionControlCommand.SetAudioEnabled:
                    {
                        bool enabled = request.AudioEnabled ?? true;
                        _audioStreamingEnabled = enabled;

                        if (_clientPaired)
                        {
                            if (enabled)
                                _ = _audioCapture.StartAsync();
                            else
                                _ = _audioCapture.StopAsync();
                        }

                        await _communication.SendSessionControlResponseAsync(new SessionControlResponse
                        {
                            RequestId = request.RequestId,
                            Command = request.Command,
                            Success = true,
                            AppliedAudioEnabled = enabled
                        });
                        AppendAuditAction(ConnectionAuditActionType.SessionControlApplied, enabled ? "Enabled remote audio streaming." : "Disabled remote audio streaming.");
                        break;
                    }

                    default:
                        await _communication.SendSessionControlResponseAsync(new SessionControlResponse
                        {
                            RequestId = request.RequestId,
                            Command = request.Command,
                            Success = false,
                            ErrorMessage = $"Unsupported session control command: {request.Command}"
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process session control request {Command}", request.Command);

                await _communication.SendSessionControlResponseAsync(new SessionControlResponse
                {
                    RequestId = request.RequestId,
                    Command = request.Command,
                    Success = false,
                    ErrorMessage = ex.Message
                });
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
            _ = FinalizeCurrentAuditLogEntryAsync("Remote session disconnected.");
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
            _preferredImageFormat = ScreenDataFormat.Raw;
            _audioStreamingEnabled = true;
            _currentPermissions = SessionPermissionSet.CreateFullAccess();
            Interlocked.Exchange(ref _lastFrameSentAtUtcMs, 0);

            // Clear chat messages
            _messagingService.ClearMessages();
            _logger.LogDebug("Chat messages cleared on disconnect");

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

        var tuning = GetStreamTuning();
        if (!ShouldSendFrame(tuning.TargetFps))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var sendStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var frameToSend = PrepareFrameForTransmission(screenData, tuning.Format, tuning.Quality);

                // Apply adaptive bitrate / quality for subsequent captures
                _screenCapture.SetQuality(tuning.Quality);

                // Apply delta encoding
                var (encodedFrame, isDelta) = await _deltaEncoder.EncodeFrameAsync(frameToSend);

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
                        tuning.Quality,
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
        if (!_communication.IsConnected || !_clientPaired || !_audioStreamingEnabled) return;

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

    private ScreenData PrepareFrameForTransmission(ScreenData screenData, ScreenDataFormat preferredFormat, int quality)
    {
        if (preferredFormat == ScreenDataFormat.Raw || screenData.Format == preferredFormat)
            return screenData;

        if (screenData.Format != ScreenDataFormat.Raw)
            return screenData;

        if (!OperatingSystem.IsWindows())
            return screenData;

        try
        {
            var encodedBytes = EncodeRawFrame(screenData, preferredFormat, quality);
            if (encodedBytes.Length == 0)
                return screenData;

            return new ScreenData
            {
                FrameId = screenData.FrameId,
                Timestamp = screenData.Timestamp,
                Width = screenData.Width,
                Height = screenData.Height,
                Format = preferredFormat,
                Quality = quality,
                ImageData = encodedBytes
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to encode frame as {Format}; falling back to raw", preferredFormat);
            return screenData;
        }
    }

    private (int Quality, int TargetFps, ScreenDataFormat Format) GetStreamTuning()
    {
        var preferredFormat = _preferredImageFormat;
        var recommendedQuality = _perfMonitor.GetRecommendedQuality();
        var latencyMs = _perfMonitor.GetAverageLatency();
        var bandwidthBytesPerSec = _perfMonitor.GetCurrentBandwidth();
        var hasMetrics = latencyMs > 0 || bandwidthBytesPerSec > 0 || _perfMonitor.GetCurrentFps() > 0;

        if (!hasMetrics)
            return (recommendedQuality, MaxAdaptiveStreamFps, preferredFormat);

        if (latencyMs >= CriticalLatencyThresholdMs ||
            (bandwidthBytesPerSec > 0 && bandwidthBytesPerSec <= CriticalBandwidthThresholdBytesPerSec))
        {
            return (Math.Max(MinimumAdaptiveQuality, Math.Min(recommendedQuality, 50)), CriticalAdaptiveStreamFps, GetThrottledFormat(preferredFormat));
        }

        if (latencyMs >= HighLatencyThresholdMs ||
            (bandwidthBytesPerSec > 0 && bandwidthBytesPerSec <= LowBandwidthThresholdBytesPerSec))
        {
            return (Math.Max(MinimumAdaptiveQuality, Math.Min(recommendedQuality, 60)), LowBandwidthAdaptiveStreamFps, GetThrottledFormat(preferredFormat));
        }

        if (latencyMs >= ModerateLatencyThresholdMs ||
            (bandwidthBytesPerSec > 0 && bandwidthBytesPerSec <= ModerateBandwidthThresholdBytesPerSec))
        {
            return (Math.Max(MinimumAdaptiveQuality, Math.Min(recommendedQuality, 70)), ModerateAdaptiveStreamFps, GetThrottledFormat(preferredFormat));
        }

        return (recommendedQuality, MaxAdaptiveStreamFps, preferredFormat);
    }

    private bool ShouldSendFrame(int targetFps)
    {
        if (targetFps >= MaxAdaptiveStreamFps)
            return true;

        var minimumFrameSpacingMs = Math.Max(1L, 1000L / Math.Max(1, targetFps));
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var previous = Interlocked.Read(ref _lastFrameSentAtUtcMs);

        if (previous > 0 && now - previous < minimumFrameSpacingMs)
            return false;

        Interlocked.Exchange(ref _lastFrameSentAtUtcMs, now);
        return true;
    }

    private static ScreenDataFormat GetThrottledFormat(ScreenDataFormat preferredFormat)
        => preferredFormat == ScreenDataFormat.JPEG ? preferredFormat : ScreenDataFormat.JPEG;

    private static byte[] EncodeRawFrame(ScreenData screenData, ScreenDataFormat format, int quality)
    {
        using var bitmap = new Bitmap(screenData.Width, screenData.Height, PixelFormat.Format32bppArgb);
        var bounds = new Rectangle(0, 0, screenData.Width, screenData.Height);
        var bitmapData = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            Marshal.Copy(screenData.ImageData, 0, bitmapData.Scan0, screenData.ImageData.Length);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        using var stream = new MemoryStream();
        if (format == ScreenDataFormat.PNG)
        {
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        }
        else
        {
            var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            if (codec == null)
                throw new InvalidOperationException("JPEG encoder not available.");

            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 1, 100));
            bitmap.Save(stream, codec, parameters);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Called when a chat message is received from the remote client.
    /// Logs the message for debugging; UI/notification handling is done by the messaging service.
    /// </summary>
    private void OnMessageReceived(object? sender, ChatMessage message)
    {
        _logger.LogInformation(
            "Chat message received from {Sender}: {Text}",
            message.SenderName,
            message.Text);
    }

    /// <summary>
    /// Called when the client sends an input event. Relays it to the local input handler.
    /// Ignored if the client has not yet successfully paired.
    /// </summary>
    private void OnInputEventReceived(object? sender, InputEvent inputEvent)
    {
        if (!_clientPaired || !_currentPermissions.AllowRemoteInput) return;

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
        if (!_communication.IsConnected || !_clientPaired || !_currentPermissions.AllowClipboardSync) return;

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
                AppendAuditAction(ConnectionAuditActionType.ClipboardSent, $"Sent clipboard {clipboardData.ContentType} to client.");

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
        if (!_clientPaired || !_currentPermissions.AllowClipboardSync) return;

        _ = Task.Run(async () =>
        {
            try
            {
                switch (clipboardData.ContentType)
                {
                    case ClipboardContentType.Text when !string.IsNullOrEmpty(clipboardData.Text):
                        await _clipboardService.SetTextAsync(clipboardData.Text);
                        AppendAuditAction(ConnectionAuditActionType.ClipboardReceived, "Applied clipboard text from client.");
                        _logger.LogDebug("Applied clipboard text from client: {Length} chars", clipboardData.Text.Length);
                        break;

                    case ClipboardContentType.Image when clipboardData.ImageData != null:
                        await _clipboardService.SetImageAsync(clipboardData.ImageData);
                        AppendAuditAction(ConnectionAuditActionType.ClipboardReceived, "Applied clipboard image from client.");
                        _logger.LogDebug("Applied clipboard image from client: {Size} bytes", clipboardData.ImageData.Length);
                        break;

                    default:
                        _logger.LogDebug("Received clipboard data with no content");
                        break;
                }

    private bool IsSessionControlAllowed(SessionControlCommand command)
    {
        if (!_currentPermissions.AllowSessionControl)
            return false;

        return command != SessionControlCommand.SetAudioEnabled || _currentPermissions.AllowAudioStreaming;
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
