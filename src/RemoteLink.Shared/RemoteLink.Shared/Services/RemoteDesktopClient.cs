using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Connection lifecycle state for the mobile client.
/// </summary>
public enum ClientConnectionState
{
    /// <summary>Not connected to any host.</summary>
    Disconnected,

    /// <summary>TCP connection is being established.</summary>
    Connecting,

    /// <summary>TCP connected; waiting for PIN pairing to complete.</summary>
    Authenticating,

    /// <summary>Fully connected and authenticated.</summary>
    Connected
}

/// <summary>
/// Main service for the remote desktop mobile client.
/// Handles network discovery, TCP connection, and PIN-based pairing with the desktop host.
/// </summary>
public class RemoteDesktopClient : IDisposable
{
    private readonly ILogger<RemoteDesktopClient> _logger;
    private readonly INetworkDiscovery _networkDiscovery;
    private readonly Func<ICommunicationService> _commFactory;
    private readonly INatTraversalService? _natTraversalService;
    private readonly DeviceInfo? _localDevice;
    private ICommunicationService? _communicationService;
    private PresentationSessionClient? _presentationSessionClient;
    private bool _isStarted;
    private bool _isBroadcastingPresence;
    private TaskCompletionSource<PairingResponse>? _pairingTcs;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SessionControlResponse>> _pendingSessionControlRequests = new();
    private CancellationTokenSource? _autoReconnectCts;
    private Task? _autoReconnectTask;
    private DeviceInfo? _pendingAutoReconnectHost;
    private TimeSpan _pendingAutoReconnectDelay;
    private bool _isAutoReconnectPending;
    private bool _disposed;

    private const int RemoteRebootReconnectAttempts = 10;
    private static readonly TimeSpan RemoteRebootReconnectRetryDelay = TimeSpan.FromSeconds(5);

    // ── Public events ─────────────────────────────────────────────────────────

    /// <summary>Fired when a new desktop host is discovered on the network.</summary>
    public event EventHandler<DeviceInfo>? DeviceDiscovered;

    /// <summary>Fired when a previously discovered host is no longer visible.</summary>
    public event EventHandler<DeviceInfo>? DeviceLost;

    /// <summary>Fired with human-readable status updates (discovery, connection, errors).</summary>
    public event EventHandler<string>? ServiceStatusChanged;

    /// <summary>Fired when the connection lifecycle state changes.</summary>
    public event EventHandler<ClientConnectionState>? ConnectionStateChanged;

    /// <summary>Fired when a screen frame is received from the connected host.</summary>
    public event EventHandler<ScreenData>? ScreenDataReceived;

    /// <summary>Fired when pairing fails, with a human-readable reason string.</summary>
    public event EventHandler<string>? PairingFailed;

    /// <summary>Fired when the connected host publishes updated connection quality metrics.</summary>
    public event EventHandler<ConnectionQuality>? ConnectionQualityUpdated;

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>Current connection lifecycle state.</summary>
    public ClientConnectionState ConnectionState { get; private set; } = ClientConnectionState.Disconnected;

    /// <summary><c>true</c> when fully authenticated and connected to a host.</summary>
    public bool IsConnected => ConnectionState == ClientConnectionState.Connected;

    /// <summary><c>true</c> when discovery has been started.</summary>
    public bool IsStarted => _isStarted;

    /// <summary>The active communication service for the current connection, if any.</summary>
    public ICommunicationService? CurrentCommunicationService => _communicationService;

    /// <summary>The latest connection quality metrics received from the connected host.</summary>
    public ConnectionQuality? CurrentConnectionQuality { get; private set; }

    /// <summary>The latest remote system information snapshot retrieved from the connected host.</summary>
    public RemoteSystemInfo? CurrentRemoteSystemInfo { get; private set; }

    /// <summary>The effective permission set granted by the connected host.</summary>
    public SessionPermissionSet? CurrentSessionPermissions { get; private set; }

    /// <summary><c>true</c> when the current connection is a read-only presentation session.</summary>
    public bool IsPresentationSession { get; private set; }

    /// <summary><c>true</c> when the client is waiting to reconnect after a requested remote reboot.</summary>
    public bool IsAutoReconnectPending => _isAutoReconnectPending;

    /// <summary>Session token returned by the host on successful pairing.</summary>
    public string? SessionToken { get; private set; }

    /// <summary>The host device we are currently connected (or connecting) to.</summary>
    public DeviceInfo? ConnectedHost { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="RemoteDesktopClient"/>.
    /// </summary>
    /// <param name="logger">Logger (pass <c>null</c> to use <see cref="NullLogger"/>).</param>
    /// <param name="networkDiscovery">UDP discovery service.</param>
    /// <param name="commFactory">
    /// Optional factory that creates the <see cref="ICommunicationService"/> used for
    /// each connection attempt. Defaults to <c>() => new TcpCommunicationService()</c>.
    /// Inject a custom factory in tests to avoid real network I/O.
    /// </param>
    public RemoteDesktopClient(
        ILogger<RemoteDesktopClient>? logger,
        INetworkDiscovery networkDiscovery,
        Func<ICommunicationService>? commFactory = null,
        INatTraversalService? natTraversalService = null,
        DeviceInfo? localDevice = null)
    {
        _logger = logger ?? NullLogger<RemoteDesktopClient>.Instance;
        _networkDiscovery = networkDiscovery;
        _commFactory = commFactory ?? (() => new TcpCommunicationService());
        _natTraversalService = natTraversalService;
        _localDevice = localDevice;

        _networkDiscovery.DeviceDiscovered += OnDeviceDiscovered;
        _networkDiscovery.DeviceLost += OnDeviceLost;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _networkDiscovery.DeviceDiscovered -= OnDeviceDiscovered;
        _networkDiscovery.DeviceLost -= OnDeviceLost;
        ResetAutoReconnectState(cancelPendingTask: true);
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts UDP discovery — broadcasting our presence and listening for desktop hosts.
    /// </summary>
    public async Task StartAsync()
        => await StartDiscoveryCoreAsync(broadcastPresence: true);

    /// <summary>
    /// Starts UDP discovery in listen-only mode so the client can find hosts
    /// without advertising itself as a connectable host.
    /// </summary>
    public async Task StartListeningOnlyAsync()
        => await StartDiscoveryCoreAsync(broadcastPresence: false);

    private async Task StartDiscoveryCoreAsync(bool broadcastPresence)
    {
        if (_isStarted) return;

        ServiceStatusChanged?.Invoke(this, "Starting discovery...");

        try
        {
            if (broadcastPresence)
                await _networkDiscovery.StartBroadcastingAsync();

            await _networkDiscovery.StartListeningAsync();

            if (broadcastPresence && _natTraversalService is not null && _localDevice is not null)
            {
                var natDiscovery = await _natTraversalService.StartAsync(_localDevice.Port);
                ApplyNatDiscoveryResult(_localDevice, natDiscovery);
            }

            _isStarted = true;
            _isBroadcastingPresence = broadcastPresence;
            ServiceStatusChanged?.Invoke(this, "Searching for desktop hosts...");
        }
        catch (Exception ex)
        {
            try
            {
                await _networkDiscovery.StopListeningAsync();
                if (broadcastPresence)
                    await _networkDiscovery.StopBroadcastingAsync();
            }
            catch
            {
                // Ignore cleanup errors after a failed start attempt.
            }

            _logger.LogError(ex, "Error starting discovery");
            ServiceStatusChanged?.Invoke(this, $"Error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Stops discovery and disconnects from any connected host.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isStarted) return;

        ServiceStatusChanged?.Invoke(this, "Stopping...");
        await DisconnectAsync();

        try
        {
            await _networkDiscovery.StopListeningAsync();
            if (_isBroadcastingPresence)
                await _networkDiscovery.StopBroadcastingAsync();

            if (_natTraversalService is not null)
                await _natTraversalService.StopAsync();
        }
        catch { /* ignore shutdown errors */ }

        _isStarted = false;
        _isBroadcastingPresence = false;
        ServiceStatusChanged?.Invoke(this, "Stopped.");
    }

    // ── Connection ────────────────────────────────────────────────────────────

    /// <summary>
    /// Connects to <paramref name="host"/> over TCP and authenticates using
    /// the 6-digit <paramref name="pin"/> displayed on the desktop host.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the connection is established and authenticated;
    /// <c>false</c> when the connection or pairing fails.
    /// </returns>
    public async Task<bool> ConnectToHostAsync(
        DeviceInfo host,
        string pin,
        CancellationToken ct = default)
        => await ConnectToHostCoreAsync(host, pin, ct, preserveAutoReconnectState: false, suppressPairingFailure: false);

    private async Task<bool> ConnectToHostCoreAsync(
        DeviceInfo host,
        string pin,
        CancellationToken ct,
        bool preserveAutoReconnectState,
        bool suppressPairingFailure)
    {
        // Disconnect from any existing session first
        await DisconnectAsync(clearAutoReconnectState: !preserveAutoReconnectState);

        ConnectedHost = host;
        SetState(ClientConnectionState.Connecting);
        ServiceStatusChanged?.Invoke(this, $"Connecting to {host.DeviceName}...");

        // ── TCP connection ────────────────────────────────────────────────────

        var comm = _commFactory();
        _communicationService = comm;

        comm.ScreenDataReceived += OnScreenDataReceived;
        comm.ConnectionStateChanged += OnCommConnectionStateChanged;
        comm.PairingResponseReceived += OnPairingResponseReceived;
        comm.ConnectionQualityReceived += OnConnectionQualityReceived;
        comm.SessionControlResponseReceived += OnSessionControlResponseReceived;

        bool tcpConnected;
        try
        {
            tcpConnected = await comm.ConnectToDeviceAsync(host);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TCP connection to {Host} failed", host.DeviceName);
            await CleanupCommAsync(comm);
            SetState(ClientConnectionState.Disconnected);
            ReportPairingFailure($"Connection failed: {ex.Message}", suppressPairingFailure);
            return false;
        }

        if (!tcpConnected)
        {
            _logger.LogWarning("Failed to connect to {Host} at {IP}:{Port}",
                host.DeviceName, host.IPAddress, host.Port);
            await CleanupCommAsync(comm);
            SetState(ClientConnectionState.Disconnected);
            ReportPairingFailure("Could not reach host — check IP and port.", suppressPairingFailure);
            return false;
        }

        // ── PIN pairing ───────────────────────────────────────────────────────

        SetState(ClientConnectionState.Authenticating);
        ServiceStatusChanged?.Invoke(this, "Authenticating...");

        _pairingTcs = new TaskCompletionSource<PairingResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var request = new PairingRequest
        {
            ClientDeviceId = BuildDeviceId(),
            ClientInternetDeviceId = _localDevice?.InternetDeviceId,
            ClientDeviceName = BuildDeviceName(),
            Pin = pin,
            RequestedAt = DateTime.UtcNow
        };

        try
        {
            await comm.SendPairingRequestAsync(request);

            // Wait up to 10 s for the host to respond
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _pairingTcs.Task.WaitAsync(timeoutCts.Token);

            if (response.Success)
            {
                SessionToken = response.SessionToken;
                CurrentSessionPermissions = response.SessionPermissions?.Clone() ?? SessionPermissionSet.CreateFullAccess();
                IsPresentationSession = false;
                if (preserveAutoReconnectState)
                    ResetAutoReconnectState(cancelPendingTask: false);
                SetState(ClientConnectionState.Connected);
                ServiceStatusChanged?.Invoke(this, $"Connected to {host.DeviceName}");
                _logger.LogInformation("Paired with {Host}, token={Token}",
                    host.DeviceName, SessionToken?[..Math.Min(8, SessionToken?.Length ?? 0)]);
                return true;
            }
            else
            {
                var reason = FormatFailureReason(response.FailureReason, response.Message);
                _logger.LogWarning("Pairing rejected: {Reason}", reason);
                ReportPairingFailure(reason, suppressPairingFailure);
                await CleanupCommAsync(comm);
                SetState(ClientConnectionState.Disconnected);
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Pairing timed out connecting to {Host}", host.DeviceName);
            ReportPairingFailure("Pairing timed out — ensure the host is ready.", suppressPairingFailure);
            await CleanupCommAsync(comm);
            SetState(ClientConnectionState.Disconnected);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pairing error with {Host}", host.DeviceName);
            ReportPairingFailure($"Pairing error: {ex.Message}", suppressPairingFailure);
            await CleanupCommAsync(comm);
            SetState(ClientConnectionState.Disconnected);
            return false;
        }
    }

    /// <summary>
    /// Connects to a host's presentation stream using the current host PIN.
    /// </summary>
    public async Task<bool> ConnectToPresentationAsync(DeviceInfo host, string pin, CancellationToken ct = default)
    {
        await DisconnectAsync();

        ConnectedHost = host;
        SetState(ClientConnectionState.Connecting);
        ServiceStatusChanged?.Invoke(this, $"Joining presentation on {host.DeviceName}...");

        var presentationClient = new PresentationSessionClient();
        _presentationSessionClient = presentationClient;
        presentationClient.ScreenDataReceived += OnPresentationScreenDataReceived;
        presentationClient.ConnectionQualityReceived += OnPresentationConnectionQualityReceived;
        presentationClient.ConnectionStateChanged += OnPresentationConnectionStateChanged;

        try
        {
            var response = await presentationClient.ConnectAsync(
                host,
                pin,
                BuildDeviceId(),
                BuildDeviceName(),
                ct);

            if (!response.Success)
            {
                PairingFailed?.Invoke(this, response.Message);
                await CleanupPresentationClientAsync(presentationClient);
                SetState(ClientConnectionState.Disconnected);
                return false;
            }

            SessionToken = response.SessionId;
            CurrentSessionPermissions = response.SessionPermissions?.Clone() ?? SessionPermissionSet.CreateViewOnly();
            IsPresentationSession = true;
            SetState(ClientConnectionState.Connected);
            ServiceStatusChanged?.Invoke(this, $"Viewing presentation on {host.DeviceName}");
            return true;
        }
        catch (OperationCanceledException)
        {
            PairingFailed?.Invoke(this, "Joining the presentation timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join presentation on {Host}", host.DeviceName);
            PairingFailed?.Invoke(this, $"Presentation join failed: {ex.Message}");
        }

        await CleanupPresentationClientAsync(presentationClient);
        SetState(ClientConnectionState.Disconnected);
        return false;
    }

    /// <summary>
    /// Gracefully disconnects from the current host and resets state.
    /// </summary>
    public async Task DisconnectAsync()
        => await DisconnectAsync(clearAutoReconnectState: true);

    private async Task DisconnectAsync(bool clearAutoReconnectState)
    {
        if (clearAutoReconnectState)
            ResetAutoReconnectState(cancelPendingTask: true);

        var comm = Interlocked.Exchange(ref _communicationService, null);
        if (comm != null)
            await CleanupCommAsync(comm);

        var presentationClient = Interlocked.Exchange(ref _presentationSessionClient, null);
        if (presentationClient != null)
            await CleanupPresentationClientAsync(presentationClient);

        _pairingTcs?.TrySetCanceled();
        _pairingTcs = null;
        CancelPendingSessionControlRequests();
        CurrentConnectionQuality = null;
        CurrentRemoteSystemInfo = null;
        CurrentSessionPermissions = null;
        IsPresentationSession = false;

        SessionToken = null;
        ConnectedHost = null;

        if (ConnectionState != ClientConnectionState.Disconnected)
            SetState(ClientConnectionState.Disconnected);
    }

    /// <summary>
    /// Requests a remote reboot on the connected host and, when supported, schedules an automatic reconnect.
    /// </summary>
    public async Task<SessionControlResponse> RequestRemoteRebootAsync(CancellationToken ct = default)
    {
        EnsureSessionPermission(
            permissions => permissions.AllowSessionControl,
            "The host has disabled remote session controls for this session.");

        var host = ConnectedHost ?? throw new InvalidOperationException("The client is not connected to a host.");

        var response = await SendSessionControlRequestAsync(
            SessionControlCommand.RebootDevice,
            configure: null,
            ct);

        EnsureSuccessfulSessionControlResponse(response, "Failed to request a remote reboot.");

        if (response.AutoReconnectSupported == true)
        {
            int reconnectDelaySeconds = Math.Max(1, response.ReconnectDelaySeconds ?? 25);
            ScheduleAutoReconnect(host, TimeSpan.FromSeconds(reconnectDelaySeconds));
            ServiceStatusChanged?.Invoke(this, $"Remote reboot requested. Waiting {reconnectDelaySeconds} seconds before reconnecting...");
        }
        else
        {
            ServiceStatusChanged?.Invoke(this, "Remote reboot requested. Automatic reconnect is unavailable for this session.");
        }

        return response;
    }

    // ── Input forwarding ──────────────────────────────────────────────────────

    /// <summary>
    /// Sends an <see cref="InputEvent"/> to the connected desktop host.
    /// No-ops when not connected.
    /// </summary>
    public async Task SendInputEventAsync(InputEvent inputEvent)
    {
        if (_communicationService is null || !IsConnected) return;
        if (CurrentSessionPermissions?.AllowRemoteInput == false) return;

        try
        {
            await _communicationService.SendInputEventAsync(inputEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send input event of type {Type}", inputEvent.Type);
        }
    }

    /// <summary>
    /// Fetch the list of monitors currently available on the remote host.
    /// </summary>
    public async Task<(IReadOnlyList<MonitorInfo> Monitors, string? SelectedMonitorId)> GetRemoteMonitorsAsync(
        CancellationToken ct = default)
    {
        EnsureSessionPermission(
            permissions => permissions.AllowSessionControl,
            "The host has disabled remote session controls for this session.");

        var response = await SendSessionControlRequestAsync(
            SessionControlCommand.GetMonitors,
            configure: null,
            ct);

        EnsureSuccessfulSessionControlResponse(response, "Failed to retrieve remote monitors.");
        return (response.Monitors ?? new List<MonitorInfo>(), response.SelectedMonitorId);
    }

    /// <summary>
    /// Fetch a point-in-time system information snapshot from the remote host.
    /// </summary>
    public async Task<RemoteSystemInfo> GetRemoteSystemInfoAsync(CancellationToken ct = default)
    {
        EnsureSessionPermission(
            permissions => permissions.AllowSessionControl,
            "The host has disabled remote session controls for this session.");

        var response = await SendSessionControlRequestAsync(
            SessionControlCommand.GetSystemInformation,
            configure: null,
            ct);

        EnsureSuccessfulSessionControlResponse(response, "Failed to retrieve remote system information.");
        CurrentRemoteSystemInfo = response.SystemInfo
            ?? throw new InvalidOperationException("The host did not return remote system information.");

        return CurrentRemoteSystemInfo;
    }

    /// <summary>
    /// Execute a remote command or script on the connected host.
    /// </summary>
    public async Task<RemoteCommandExecutionResult> ExecuteRemoteCommandAsync(
        string commandText,
        RemoteCommandShell shell = RemoteCommandShell.PowerShell,
        int timeoutSeconds = 30,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            throw new ArgumentException("Command text is required.", nameof(commandText));

        EnsureSessionPermission(
            permissions => permissions.AllowSessionControl,
            "The host has disabled remote session controls for this session.");

        var response = await SendSessionControlRequestAsync(
            SessionControlCommand.ExecuteCommand,
            request => request.CommandRequest = new RemoteCommandExecutionRequest
            {
                CommandText = commandText,
                Shell = shell,
                WorkingDirectory = workingDirectory,
                TimeoutSeconds = Math.Clamp(timeoutSeconds, 1, 300)
            },
            ct);

        EnsureSuccessfulSessionControlResponse(response, "Failed to execute remote command.");
        return response.CommandResult
            ?? throw new InvalidOperationException("The host did not return a remote command result.");
    }

    /// <summary>
    /// Select a monitor on the remote host.
    /// </summary>
    public async Task<string?> SelectRemoteMonitorAsync(string monitorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(monitorId))
            throw new ArgumentException("Monitor ID is required.", nameof(monitorId));

        EnsureSessionPermission(
            permissions => permissions.AllowSessionControl,
            "The host has disabled remote session controls for this session.");

        var response = await SendSessionControlRequestAsync(
            SessionControlCommand.SelectMonitor,
            request => request.MonitorId = monitorId,
            ct);

        EnsureSuccessfulSessionControlResponse(response, "Failed to switch remote monitor.");
        return response.SelectedMonitorId;
    }

    /// <summary>
    /// Set the remote host capture quality.
    /// </summary>
    public async Task<int> SetRemoteQualityAsync(int quality, CancellationToken ct = default)
    {
        EnsureSessionPermission(
            permissions => permissions.AllowSessionControl,
            "The host has disabled remote session controls for this session.");

        var clamped = Math.Clamp(quality, 0, 100);

        var response = await SendSessionControlRequestAsync(
            SessionControlCommand.SetQuality,
            request => request.Quality = clamped,
            ct);

        EnsureSuccessfulSessionControlResponse(response, "Failed to set remote quality.");
        return response.AppliedQuality ?? clamped;
    }

    /// <summary>
    /// Set the preferred image format on the remote host.
    /// </summary>
    public async Task<ScreenDataFormat> SetRemoteImageFormatAsync(ScreenDataFormat format, CancellationToken ct = default)
    {
        EnsureSessionPermission(
            permissions => permissions.AllowSessionControl,
            "The host has disabled remote session controls for this session.");

        var response = await SendSessionControlRequestAsync(
            SessionControlCommand.SetImageFormat,
            request => request.ImageFormat = format,
            ct);

        EnsureSuccessfulSessionControlResponse(response, "Failed to set remote image format.");
        return response.AppliedImageFormat ?? format;
    }

    /// <summary>
    /// Enable or disable remote audio streaming on the host.
    /// </summary>
    public async Task<bool> SetRemoteAudioEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        EnsureSessionPermission(
            permissions => permissions.AllowSessionControl && permissions.AllowAudioStreaming,
            "The host has disabled remote audio controls for this session.");

        var response = await SendSessionControlRequestAsync(
            SessionControlCommand.SetAudioEnabled,
            request => request.AudioEnabled = enabled,
            ct);

        EnsureSuccessfulSessionControlResponse(response, "Failed to update remote audio streaming.");
        return response.AppliedAudioEnabled ?? enabled;
    }

    /// <summary>
    /// Returns the latest local NAT traversal candidates, starting discovery if needed.
    /// </summary>
    public async Task<NatDiscoveryResult?> GetNatTraversalInfoAsync(CancellationToken ct = default)
    {
        if (_natTraversalService is null || _localDevice is null)
            return null;

        var result = _natTraversalService.IsRunning
            ? await _natTraversalService.RefreshCandidatesAsync(ct)
            : await _natTraversalService.StartAsync(_localDevice.Port, ct);

        ApplyNatDiscoveryResult(_localDevice, result);
        return result;
    }

    /// <summary>
    /// Attempts a direct UDP hole-punch connectivity check against a remote host.
    /// </summary>
    public async Task<NatTraversalConnectResult> TryNatTraversalAsync(DeviceInfo host, CancellationToken ct = default)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        if (_natTraversalService is null || _localDevice is null)
        {
            return new NatTraversalConnectResult
            {
                Success = false,
                FailureReason = "NAT traversal is not configured for this client."
            };
        }

        if (!_natTraversalService.IsRunning)
        {
            var discovery = await _natTraversalService.StartAsync(_localDevice.Port, ct);
            ApplyNatDiscoveryResult(_localDevice, discovery);
        }

        var candidates = BuildRemoteNatCandidates(host);
        if (candidates.Count == 0)
        {
            return new NatTraversalConnectResult
            {
                Success = false,
                FailureReason = "The remote host has not published any NAT traversal candidates."
            };
        }

        return await _natTraversalService.TryConnectAsync(candidates, ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void SetState(ClientConnectionState state)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke(this, state);
    }

    private void OnCommConnectionStateChanged(object? sender, bool connected)
    {
        // The TCP layer dropped unexpectedly while we thought we were connected
        if (!connected && ConnectionState == ClientConnectionState.Connected && !IsPresentationSession)
        {
            _logger.LogWarning("Lost TCP connection to {Host}", ConnectedHost?.DeviceName);
            CancelPendingSessionControlRequests();
            CurrentConnectionQuality = null;
            SetState(ClientConnectionState.Disconnected);

            if (_isAutoReconnectPending)
            {
                ServiceStatusChanged?.Invoke(this, "Remote host is restarting. Automatic reconnect is pending...");
                StartAutoReconnectLoopIfNeeded();
            }
            else
            {
                ServiceStatusChanged?.Invoke(this, "Disconnected from host.");
            }
        }
    }

    private void OnPresentationConnectionStateChanged(object? sender, bool connected)
    {
        if (!connected && ConnectionState == ClientConnectionState.Connected && IsPresentationSession)
        {
            CurrentConnectionQuality = null;
            CurrentSessionPermissions = null;
            IsPresentationSession = false;
            SetState(ClientConnectionState.Disconnected);
            ServiceStatusChanged?.Invoke(this, "Presentation ended.");
        }
    }

    private void OnPairingResponseReceived(object? sender, PairingResponse response)
    {
        _pairingTcs?.TrySetResult(response);
    }

    private void OnConnectionQualityReceived(object? sender, ConnectionQuality quality)
    {
        CurrentConnectionQuality = quality;
        ConnectionQualityUpdated?.Invoke(this, quality);
    }

    private void OnSessionControlResponseReceived(object? sender, SessionControlResponse response)
    {
        if (_pendingSessionControlRequests.TryRemove(response.RequestId, out var tcs))
            tcs.TrySetResult(response);
    }

    private void OnScreenDataReceived(object? sender, ScreenData screenData)
    {
        ScreenDataReceived?.Invoke(this, screenData);
    }

    private void OnPresentationScreenDataReceived(object? sender, ScreenData screenData)
    {
        ScreenDataReceived?.Invoke(this, screenData);
    }

    private void OnPresentationConnectionQualityReceived(object? sender, ConnectionQuality quality)
    {
        CurrentConnectionQuality = quality;
        ConnectionQualityUpdated?.Invoke(this, quality);
    }

    private void OnDeviceDiscovered(object? sender, DeviceInfo device)
    {
        if (device.Type == DeviceType.Desktop)
        {
            _logger.LogInformation("Discovered {Name} at {IP}:{Port}",
                device.DeviceName, device.IPAddress, device.Port);
            DeviceDiscovered?.Invoke(this, device);
        }
    }

    private void OnDeviceLost(object? sender, DeviceInfo device)
    {
        if (device.Type == DeviceType.Desktop)
        {
            _logger.LogInformation("Lost {Name}", device.DeviceName);
            DeviceLost?.Invoke(this, device);
        }
    }

    private async Task CleanupCommAsync(ICommunicationService comm)
    {
        comm.ScreenDataReceived -= OnScreenDataReceived;
        comm.ConnectionStateChanged -= OnCommConnectionStateChanged;
        comm.PairingResponseReceived -= OnPairingResponseReceived;
        comm.ConnectionQualityReceived -= OnConnectionQualityReceived;
        comm.SessionControlResponseReceived -= OnSessionControlResponseReceived;

        try { await comm.DisconnectAsync(); } catch { /* ignore */ }
        if (comm is IDisposable d) d.Dispose();
    }

    private void ReportPairingFailure(string message, bool suppressPairingFailure)
    {
        if (!suppressPairingFailure)
            PairingFailed?.Invoke(this, message);
    }

    private void ScheduleAutoReconnect(DeviceInfo host, TimeSpan delay)
    {
        ResetAutoReconnectState(cancelPendingTask: true);
        _pendingAutoReconnectHost = host;
        _pendingAutoReconnectDelay = delay;
        _isAutoReconnectPending = true;
        _autoReconnectCts = new CancellationTokenSource();
    }

    private void StartAutoReconnectLoopIfNeeded()
    {
        if (!_isAutoReconnectPending || _pendingAutoReconnectHost is null)
            return;

        if (_autoReconnectTask is not null && !_autoReconnectTask.IsCompleted)
            return;

        _autoReconnectTask = Task.Run(RunAutoReconnectLoopAsync);
    }

    private async Task RunAutoReconnectLoopAsync()
    {
        var host = _pendingAutoReconnectHost;
        var token = _autoReconnectCts?.Token ?? CancellationToken.None;
        if (host is null)
            return;

        try
        {
            if (_pendingAutoReconnectDelay > TimeSpan.Zero)
                await Task.Delay(_pendingAutoReconnectDelay, token);

            for (int attempt = 1; attempt <= RemoteRebootReconnectAttempts; attempt++)
            {
                ServiceStatusChanged?.Invoke(this, $"Attempting automatic reconnect ({attempt}/{RemoteRebootReconnectAttempts})...");

                if (await ConnectToHostCoreAsync(host, string.Empty, token, preserveAutoReconnectState: true, suppressPairingFailure: true))
                {
                    ServiceStatusChanged?.Invoke(this, $"Reconnected to {host.DeviceName} after remote reboot.");
                    return;
                }

                if (attempt < RemoteRebootReconnectAttempts)
                    await Task.Delay(RemoteRebootReconnectRetryDelay, token);
            }

            ResetAutoReconnectState(cancelPendingTask: true);
            PairingFailed?.Invoke(this, "Automatic reconnect failed. Ensure the host starts automatically and this device is trusted.");
            ServiceStatusChanged?.Invoke(this, "Automatic reconnect failed.");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _autoReconnectTask = null;
        }
    }

    private void ResetAutoReconnectState(bool cancelPendingTask)
    {
        _isAutoReconnectPending = false;
        _pendingAutoReconnectHost = null;
        _pendingAutoReconnectDelay = TimeSpan.Zero;

        var cts = Interlocked.Exchange(ref _autoReconnectCts, null);
        if (cts is null)
            return;

        if (cancelPendingTask)
            cts.Cancel();

        cts.Dispose();
    }

    private async Task CleanupPresentationClientAsync(PresentationSessionClient presentationClient)
    {
        presentationClient.ScreenDataReceived -= OnPresentationScreenDataReceived;
        presentationClient.ConnectionQualityReceived -= OnPresentationConnectionQualityReceived;
        presentationClient.ConnectionStateChanged -= OnPresentationConnectionStateChanged;

        try
        {
            await presentationClient.DisconnectAsync();
        }
        catch
        {
        }

        await presentationClient.DisposeAsync();
    }

    private async Task<SessionControlResponse> SendSessionControlRequestAsync(
        SessionControlCommand command,
        Action<SessionControlRequest>? configure,
        CancellationToken ct)
    {
        var comm = _communicationService;
        if (comm is null || !IsConnected)
            throw new InvalidOperationException("The client is not connected to a host.");

        var request = new SessionControlRequest { Command = command };
        configure?.Invoke(request);

        var tcs = new TaskCompletionSource<SessionControlResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingSessionControlRequests[request.RequestId] = tcs;

        try
        {
            await comm.SendSessionControlRequestAsync(request);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            return await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch
        {
            _pendingSessionControlRequests.TryRemove(request.RequestId, out _);
            throw;
        }
    }

    private void CancelPendingSessionControlRequests()
    {
        foreach (var pending in _pendingSessionControlRequests)
        {
            if (_pendingSessionControlRequests.TryRemove(pending.Key, out var tcs))
                tcs.TrySetCanceled();
        }
    }

    private static void EnsureSuccessfulSessionControlResponse(SessionControlResponse response, string fallbackMessage)
    {
        if (!response.Success)
            throw new InvalidOperationException(response.ErrorMessage ?? fallbackMessage);
    }

    private void EnsureSessionPermission(Func<SessionPermissionSet, bool> predicate, string message)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var permissions = CurrentSessionPermissions;
        if (permissions is null)
            return;

        if (!predicate(permissions))
            throw new InvalidOperationException(message);
    }

    private static string FormatFailureReason(PairingFailureReason? reason, string? message)
    {
        return reason switch
        {
            PairingFailureReason.InvalidPin      => "Incorrect PIN. Please try again.",
            PairingFailureReason.PinExpired      => "PIN has expired. Ask the host to refresh it.",
            PairingFailureReason.TooManyAttempts => "Too many failed attempts. Host PIN is locked.",
            PairingFailureReason.HostRefused     => "Connection refused by the host.",
            _                                    => message ?? "Unknown pairing failure."
        };
    }

    private string BuildDeviceId()
    {
        if (!string.IsNullOrWhiteSpace(_localDevice?.DeviceId))
            return _localDevice.DeviceId;

        var raw = $"{Environment.MachineName}_Mobile_{Guid.NewGuid():N}";
        return raw[..Math.Min(48, raw.Length)];
    }

    private string BuildDeviceName() =>
        _localDevice?.DeviceName ?? $"{Environment.MachineName} (Mobile)";

    private static List<NatEndpointCandidate> BuildRemoteNatCandidates(DeviceInfo host)
    {
        var candidates = host.NatCandidates?
            .Where(candidate =>
                candidate.Port > 0 &&
                !string.IsNullOrWhiteSpace(candidate.IPAddress) &&
                string.Equals(candidate.Protocol, "udp", StringComparison.OrdinalIgnoreCase))
            .Select(CloneCandidate)
            .ToList() ?? new List<NatEndpointCandidate>();

        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(host.PublicIPAddress) && host.PublicPort is > 0)
        {
            candidates.Add(new NatEndpointCandidate
            {
                IPAddress = host.PublicIPAddress,
                Port = host.PublicPort.Value,
                Type = NatCandidateType.ServerReflexive,
                Priority = 100,
                Source = "device-info"
            });
        }

        return candidates;
    }

    private static void ApplyNatDiscoveryResult(DeviceInfo device, NatDiscoveryResult result)
    {
        device.PublicIPAddress = result.PublicIPAddress;
        device.PublicPort = result.PublicPort;
        device.NatType = result.NatType;
        device.NatCandidates = result.Candidates.Select(CloneCandidate).ToList();
    }

    private static NatEndpointCandidate CloneCandidate(NatEndpointCandidate candidate) =>
        new()
        {
            CandidateId = candidate.CandidateId,
            IPAddress = candidate.IPAddress,
            Port = candidate.Port,
            Protocol = candidate.Protocol,
            Type = candidate.Type,
            Priority = candidate.Priority,
            Source = candidate.Source
        };
}
