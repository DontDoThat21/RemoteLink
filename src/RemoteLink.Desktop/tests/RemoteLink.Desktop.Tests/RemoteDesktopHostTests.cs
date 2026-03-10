using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Tests;

// ── Test Doubles ──────────────────────────────────────────────────────────────
// Hand-rolled fakes (no mocking framework needed).

/// <summary>In-memory fake for <see cref="ICommunicationService"/> used in host tests.</summary>
internal sealed class FakeCommunicationService : ICommunicationService, IDisposable
{
    // Thread-safe captured outbound messages (accessed from concurrent Task.Run)
    private readonly object _lock = new();
    private readonly List<ScreenData> _sentScreenData = new();
    private readonly List<InputEvent> _sentInputEvents = new();
    private readonly List<PairingResponse> _sentPairingResponses = new();
    private readonly List<ConnectionQuality> _sentConnectionQuality = new();
    private readonly List<SessionControlResponse> _sentSessionControlResponses = new();
    private readonly List<ClipboardData> _sentClipboardData = new();
    private readonly List<ChatMessage> _sentChatMessages = new();
    private readonly List<string> _sentMessageReadAcks = new();

    public List<ScreenData> SentScreenData
    {
        get { lock (_lock) { return new List<ScreenData>(_sentScreenData); } }
    }
    public List<InputEvent> SentInputEvents
    {
        get { lock (_lock) { return new List<InputEvent>(_sentInputEvents); } }
    }
    public List<PairingResponse> SentPairingResponses
    {
        get { lock (_lock) { return new List<PairingResponse>(_sentPairingResponses); } }
    }
    public List<ConnectionQuality> SentConnectionQuality
    {
        get { lock (_lock) { return new List<ConnectionQuality>(_sentConnectionQuality); } }
    }
    public List<SessionControlResponse> SentSessionControlResponses
    {
        get { lock (_lock) { return new List<SessionControlResponse>(_sentSessionControlResponses); } }
    }
    public List<ClipboardData> SentClipboardData
    {
        get { lock (_lock) { return new List<ClipboardData>(_sentClipboardData); } }
    }
    public List<ChatMessage> SentChatMessages
    {
        get { lock (_lock) { return new List<ChatMessage>(_sentChatMessages); } }
    }
    public List<string> SentMessageReadAcks
    {
        get { lock (_lock) { return new List<string>(_sentMessageReadAcks); } }
    }

    // Settable connection state — tests can toggle this
    public bool IsConnected { get; set; }

    // ── ICommunicationService events ──────────────────────────────────────────
    public event EventHandler<ScreenData>? ScreenDataReceived;
    public event EventHandler<InputEvent>? InputEventReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<PairingRequest>? PairingRequestReceived;
    public event EventHandler<PairingResponse>? PairingResponseReceived;
    public event EventHandler<ConnectionQuality>? ConnectionQualityReceived;
    public event EventHandler<SessionControlRequest>? SessionControlRequestReceived;
    public event EventHandler<SessionControlResponse>? SessionControlResponseReceived;
    public event EventHandler<ClipboardData>? ClipboardDataReceived;
    public event EventHandler<AudioData>? AudioDataReceived;
    public event EventHandler<FileTransferRequest>? FileTransferRequestReceived;
    public event EventHandler<FileTransferResponse>? FileTransferResponseReceived;
    public event EventHandler<FileTransferChunk>? FileTransferChunkReceived;
    public event EventHandler<FileTransferComplete>? FileTransferCompleteReceived;
    public event EventHandler<ChatMessage>? ChatMessageReceived;
    public event EventHandler<string>? MessageReadReceived;
    public event EventHandler<PrintJob>? PrintJobReceived;
    public event EventHandler<PrintJobResponse>? PrintJobResponseReceived;
    public event EventHandler<PrintJobStatus>? PrintJobStatusReceived;

    // ── ICommunicationService methods ─────────────────────────────────────────
    public Task StartAsync(int port) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task<bool> ConnectToDeviceAsync(DeviceInfo device) => Task.FromResult(true);
    public Task DisconnectAsync() => Task.CompletedTask;

    public Task SendScreenDataAsync(ScreenData screenData)
    {
        lock (_lock) { _sentScreenData.Add(screenData); }
        return Task.CompletedTask;
    }

    public Task SendInputEventAsync(InputEvent inputEvent)
    {
        lock (_lock) { _sentInputEvents.Add(inputEvent); }
        return Task.CompletedTask;
    }

    public Task SendPairingRequestAsync(PairingRequest request) => Task.CompletedTask;

    public Task SendPairingResponseAsync(PairingResponse response)
    {
        lock (_lock) { _sentPairingResponses.Add(response); }
        return Task.CompletedTask;
    }

    public Task SendConnectionQualityAsync(ConnectionQuality quality)
    {
        lock (_lock) { _sentConnectionQuality.Add(quality); }
        return Task.CompletedTask;
    }

    public Task SendSessionControlRequestAsync(SessionControlRequest request) => Task.CompletedTask;

    public Task SendSessionControlResponseAsync(SessionControlResponse response)
    {
        lock (_lock) { _sentSessionControlResponses.Add(response); }
        return Task.CompletedTask;
    }

    public Task SendClipboardDataAsync(ClipboardData clipboardData)
    {
        lock (_lock) { _sentClipboardData.Add(clipboardData); }
        return Task.CompletedTask;
    }

    public Task SendAudioDataAsync(AudioData audioData) => Task.CompletedTask;

    public Task SendFileTransferRequestAsync(FileTransferRequest request) => Task.CompletedTask;
    public Task SendFileTransferResponseAsync(FileTransferResponse response) => Task.CompletedTask;
    public Task SendFileTransferChunkAsync(FileTransferChunk chunk) => Task.CompletedTask;
    public Task SendFileTransferCompleteAsync(FileTransferComplete complete) => Task.CompletedTask;

    public Task SendChatMessageAsync(ChatMessage message)
    {
        lock (_lock) { _sentChatMessages.Add(message); }
        return Task.CompletedTask;
    }

    public Task SendMessageReadAsync(string messageId)
    {
        lock (_lock) { _sentMessageReadAcks.Add(messageId); }
        return Task.CompletedTask;
    }

    public Task SendPrintJobAsync(PrintJob printJob) => Task.CompletedTask;
    public Task SendPrintJobResponseAsync(PrintJobResponse response) => Task.CompletedTask;
    public Task SendPrintJobStatusAsync(PrintJobStatus status) => Task.CompletedTask;

    // ── Test helpers — raise events on behalf of a remote client ─────────────

    /// <summary>Simulate a client connecting or disconnecting.</summary>
    public void RaiseConnectionStateChanged(bool connected)
    {
        IsConnected = connected;
        ConnectionStateChanged?.Invoke(this, connected);
    }

    /// <summary>Simulate a pairing request from a remote client.</summary>
    public void RaisePairingRequest(PairingRequest request)
        => PairingRequestReceived?.Invoke(this, request);

    /// <summary>Simulate an input event from a remote client.</summary>
    public void RaiseInputEventReceived(InputEvent inputEvent)
        => InputEventReceived?.Invoke(this, inputEvent);

    public void RaiseSessionControlRequest(SessionControlRequest request)
        => SessionControlRequestReceived?.Invoke(this, request);

    public void Dispose() { }
}

internal sealed class FakeConnectionRequestNotificationPublisher : IConnectionRequestNotificationPublisher
{
    private readonly object _lock = new();
    private readonly List<IncomingConnectionRequestAlert> _alerts = new();

    public List<IncomingConnectionRequestAlert> Alerts
    {
        get { lock (_lock) { return new List<IncomingConnectionRequestAlert>(_alerts); } }
    }

    public Task PublishAsync(IncomingConnectionRequestAlert alert, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _alerts.Add(alert);
        }

        return Task.CompletedTask;
    }
}

/// <summary>In-memory fake for <see cref="IScreenCapture"/>.</summary>
internal sealed class FakeScreenCapture : IScreenCapture
{
    public bool IsCapturing { get; private set; }
    public int StartCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public int LastQuality { get; private set; } = 75;
    public string? SelectedMonitorId { get; private set; }

    public event EventHandler<ScreenData>? FrameCaptured;

    public Task StartCaptureAsync()
    {
        IsCapturing = true;
        StartCallCount++;
        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        IsCapturing = false;
        StopCallCount++;
        return Task.CompletedTask;
    }

    public Task<ScreenData> CaptureFrameAsync()
        => Task.FromResult(new ScreenData { Width = 1, Height = 1 });

    public Task<(int Width, int Height)> GetScreenDimensionsAsync()
        => Task.FromResult((1920, 1080));

    public void SetQuality(int quality) => LastQuality = quality;

    public Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync()
    {
        var monitor = new MonitorInfo
        {
            Id = "fake-monitor",
            Name = "Fake Monitor",
            IsPrimary = true,
            Width = 1920,
            Height = 1080,
            Left = 0,
            Top = 0
        };
        return Task.FromResult<IReadOnlyList<MonitorInfo>>(new[] { monitor });
    }

    public Task<bool> SelectMonitorAsync(string monitorId)
    {
        SelectedMonitorId = monitorId == "fake-monitor" ? monitorId : null;
        return Task.FromResult(SelectedMonitorId != null);
    }

    public string? GetSelectedMonitorId()
        => SelectedMonitorId;

    /// <summary>Manually fire a <see cref="FrameCaptured"/> event.</summary>
    public void RaiseFrameCaptured(ScreenData data)
        => FrameCaptured?.Invoke(this, data);
}

/// <summary>In-memory fake for <see cref="IInputHandler"/>.</summary>
internal sealed class FakeInputHandler : IInputHandler
{
    // Use a lock to guard against concurrent Task.Run calls from RemoteDesktopHost
    private readonly object _lock = new();
    private readonly List<InputEvent> _receivedEvents = new();

    /// <summary>Thread-safe snapshot of events received so far.</summary>
    public List<InputEvent> ReceivedEvents
    {
        get { lock (_lock) { return new List<InputEvent>(_receivedEvents); } }
    }

    public bool IsActive { get; private set; }

    public Task StartAsync()
    {
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    public Task ProcessInputEventAsync(InputEvent inputEvent)
    {
        lock (_lock) { _receivedEvents.Add(inputEvent); }
        return Task.CompletedTask;
    }

    public Task SendShortcutAsync(KeyboardShortcut shortcut)
    {
        // Track shortcut sends if needed in the future
        return Task.CompletedTask;
    }
}

/// <summary>In-memory fake for <see cref="IPairingService"/>.</summary>
internal sealed class FakePairingService : IPairingService
{
    private string _currentPin = "123456";

    /// <summary>Controls whether <see cref="ValidatePin"/> returns true.</summary>
    public bool ValidatePinResult { get; set; } = true;
    public int ValidatePinCallCount { get; private set; }

    public bool IsLockedOut { get; set; }
    public string? CurrentPin => _currentPin;
    public bool IsPinExpired => false;
    public int AttemptsRemaining => IsLockedOut ? 0 : 5;
    public int MaxAttempts => 5;

    public event EventHandler<string>? PinGenerated;
    public event EventHandler<PairingAttemptResult>? PairingAttempted;

    public string GeneratePin()
    {
        _currentPin = "123456";
        PinGenerated?.Invoke(this, _currentPin);
        return _currentPin;
    }

    public void RefreshPin() => GeneratePin();

    public bool ValidatePin(string pin)
    {
        ValidatePinCallCount++;
        bool result = ValidatePinResult && !IsLockedOut;
        PairingAttempted?.Invoke(this, new PairingAttemptResult { Success = result });
        return result;
    }
}

internal sealed class FakeUserAccountService : IUserAccountService
{
    private readonly List<AccountManagedDevice> _managedDevices = new();
    private readonly HashSet<string> _trustedDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    public UserAccountSession? CurrentSession { get; private set; }
    public bool IsSignedIn { get; private set; }
    public event EventHandler<UserAccountSession?>? SessionChanged;

    public List<AccountManagedDevice> ManagedDevices => _managedDevices.Select(device => new AccountManagedDevice
    {
        DeviceId = device.DeviceId,
        InternetDeviceId = device.InternetDeviceId,
        DeviceName = device.DeviceName,
        Type = device.Type,
        IPAddress = device.IPAddress,
        Port = device.Port,
        SupportsRelay = device.SupportsRelay,
        RelayServerHost = device.RelayServerHost,
        RelayServerPort = device.RelayServerPort,
        IsTrusted = device.IsTrusted,
        TrustedAtUtc = device.TrustedAtUtc,
        LastSeenAtUtc = device.LastSeenAtUtc
    }).ToList();

    public void SignIn()
    {
        CurrentSession = new UserAccountSession
        {
            AccountId = "account-1",
            Email = "alice@example.com",
            DisplayName = "Alice",
            SessionToken = "session-token",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
        };
        IsSignedIn = true;
        SessionChanged?.Invoke(this, CurrentSession);
    }

    public void TrustDevice(string deviceId, string? internetDeviceId = null)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
            _trustedDeviceIds.Add(deviceId);

        var normalizedInternetId = DeviceIdentityManager.NormalizeInternetDeviceId(internetDeviceId);
        if (normalizedInternetId is not null)
            _trustedDeviceIds.Add(normalizedInternetId);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<UserAccountSession> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<UserAccountSession> LoginAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<UserAccountSession> LoginAsync(string email, string password, string twoFactorCode, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<UserAccountProfile?> GetCurrentProfileAsync(CancellationToken cancellationToken = default) => Task.FromResult<UserAccountProfile?>(null);

    public Task RegisterDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        var existing = _managedDevices.FirstOrDefault(candidate =>
            string.Equals(candidate.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(DeviceIdentityManager.NormalizeInternetDeviceId(candidate.InternetDeviceId), DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId), StringComparison.Ordinal));

        if (existing is null)
        {
            _managedDevices.Add(new AccountManagedDevice
            {
                DeviceId = device.DeviceId,
                InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId),
                DeviceName = device.DeviceName,
                Type = device.Type,
                IPAddress = device.IPAddress,
                Port = device.Port,
                IsTrusted = _trustedDeviceIds.Contains(device.DeviceId) ||
                            (DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId) is string normalizedInternetId && _trustedDeviceIds.Contains(normalizedInternetId)),
                TrustedAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            });
        }

        return Task.CompletedTask;
    }

    public Task RemoveManagedDeviceAsync(string deviceIdentifier, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<AccountManagedDevice>> GetManagedDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AccountManagedDevice>>(ManagedDevices.AsReadOnly());
    public Task SetDeviceTrustAsync(string deviceIdentifier, bool isTrusted, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> IsDeviceTrustedAsync(string deviceIdentifier, string? internetDeviceId = null, CancellationToken cancellationToken = default)
    {
        var normalizedInternetId = DeviceIdentityManager.NormalizeInternetDeviceId(internetDeviceId);
        return Task.FromResult(
            (!string.IsNullOrWhiteSpace(deviceIdentifier) && _trustedDeviceIds.Contains(deviceIdentifier)) ||
            (normalizedInternetId is not null && _trustedDeviceIds.Contains(normalizedInternetId)));
    }

    public Task SyncSavedDevicesAsync(IEnumerable<SavedDevice> savedDevices, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<SavedDevice>> GetSyncedSavedDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SavedDevice>>(Array.Empty<SavedDevice>());
    public Task<UserAccountTwoFactorSetup> BeginTwoFactorSetupAsync(string? issuer = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task EnableTwoFactorAsync(string verificationCode, bool requireForUnattendedAccess = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task DisableTwoFactorAsync(string verificationCode, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<bool> IsTwoFactorRequiredForUnattendedAccessAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> ValidateTwoFactorCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

/// <summary>In-memory fake for <see cref="INetworkDiscovery"/>.</summary>
internal sealed class FakeNetworkDiscovery : INetworkDiscovery
{
    public event EventHandler<DeviceInfo>? DeviceDiscovered;
    public event EventHandler<DeviceInfo>? DeviceLost;

    public Task StartBroadcastingAsync() => Task.CompletedTask;
    public Task StopBroadcastingAsync() => Task.CompletedTask;
    public Task StartListeningAsync() => Task.CompletedTask;
    public Task StopListeningAsync() => Task.CompletedTask;

    public Task<IEnumerable<DeviceInfo>> GetDiscoveredDevicesAsync()
        => Task.FromResult(Enumerable.Empty<DeviceInfo>());
}

/// <summary>In-memory fake for <see cref="ISessionManager"/> used in host tests.</summary>
internal sealed class FakeSessionManager : ISessionManager
{
    private readonly List<RemoteSession> _sessions = new();
    private readonly object _lock = new();

    public List<RemoteSession> CreatedSessions
    {
        get { lock (_lock) { return new List<RemoteSession>(_sessions); } }
    }

    // ── ISessionManager events ────────────────────────────────────────────────
    public event EventHandler<RemoteSession>? SessionCreated;
    public event EventHandler<RemoteSession>? SessionConnected;
    public event EventHandler<RemoteSession>? SessionDisconnected;
    public event EventHandler<RemoteSession>? SessionEnded;
    public event EventHandler<RemoteSession>? ReconnectFailed;

    // ── ISessionManager methods ───────────────────────────────────────────────
    public RemoteSession CreateSession(
        string hostId, string hostDeviceName,
        string clientId, string clientDeviceName)
    {
        var session = new RemoteSession
        {
            SessionId = Guid.NewGuid().ToString(),
            HostId = hostId,
            HostDeviceName = hostDeviceName,
            ClientId = clientId,
            ClientDeviceName = clientDeviceName,
            CreatedAt = DateTime.UtcNow,
            Status = SessionStatus.Pending
        };
        lock (_lock) { _sessions.Add(session); }
        SessionCreated?.Invoke(this, session);
        return session;
    }

    public RemoteSession? GetSession(string sessionId)
    {
        lock (_lock) { return _sessions.FirstOrDefault(s => s.SessionId == sessionId); }
    }

    public IReadOnlyList<RemoteSession> GetAllSessions()
    {
        lock (_lock) { return new List<RemoteSession>(_sessions).AsReadOnly(); }
    }

    public RemoteSession? GetActiveSession()
    {
        lock (_lock) { return _sessions.FirstOrDefault(s => s.Status == SessionStatus.Connected); }
    }

    public void OnConnected(string sessionId)
    {
        RemoteSession? session;
        lock (_lock)
        {
            session = GetSession(sessionId);
            if (session != null)
            {
                session.Status = SessionStatus.Connected;
                session.LastConnectedAt = DateTime.UtcNow;
                session.ReconnectAttempts = 0;
            }
        }
        if (session != null) SessionConnected?.Invoke(this, session);
    }

    public void OnDisconnected(string sessionId, string? reason = null)
    {
        RemoteSession? session;
        lock (_lock)
        {
            session = GetSession(sessionId);
            if (session != null)
            {
                session.Status = SessionStatus.Disconnected;
                session.DisconnectedAt = DateTime.UtcNow;
                session.DisconnectReason = reason;
            }
        }
        if (session != null) SessionDisconnected?.Invoke(this, session);
    }

    public bool TryReconnect(string sessionId)
    {
        RemoteSession? session;
        bool accepted;
        lock (_lock)
        {
            session = GetSession(sessionId);
            if (session == null) return false;

            session.ReconnectAttempts++;
            accepted = session.ReconnectAttempts <= session.MaxReconnectAttempts;
            session.Status = accepted ? SessionStatus.Pending : SessionStatus.Error;
        }
        if (!accepted && session != null) ReconnectFailed?.Invoke(this, session);
        return accepted;
    }

    public void EndSession(string sessionId)
    {
        RemoteSession? session;
        lock (_lock)
        {
            session = GetSession(sessionId);
            if (session != null) session.Status = SessionStatus.Ended;
        }
        if (session != null) SessionEnded?.Invoke(this, session);
    }
}

/// <summary>In-memory fake for <see cref="IDeltaFrameEncoder"/> used in host tests.</summary>
internal sealed class FakeDeltaFrameEncoder : IDeltaFrameEncoder
{
    public bool ResetCalled { get; private set; }

    public Task<(ScreenData EncodedFrame, bool IsDelta)> EncodeFrameAsync(ScreenData currentFrame)
    {
        // Just pass through the frame unmodified (no actual delta encoding in tests)
        return Task.FromResult((currentFrame, false));
    }

    public void Reset()
    {
        ResetCalled = true;
    }

    public void SetDeltaThreshold(int percentageThreshold)
    {
        // No-op in fake
    }
}

/// <summary>In-memory fake for <see cref="IPerformanceMonitor"/> used in host tests.</summary>
internal sealed class FakePerformanceMonitor : IPerformanceMonitor
{
    private int _recommendedQuality = 75;
    private double _currentFps = 10.0;
    private long _currentBandwidth = 3_000_000;
    private long _averageLatency = 20;
    public bool ResetCalled { get; private set; }
    public int RecordFrameSentCallCount { get; private set; }

    public void RecordFrameSent(int frameBytes, long latencyMs)
    {
        RecordFrameSentCallCount++;
    }

    public int GetRecommendedQuality() => _recommendedQuality;

    public double GetCurrentFps() => _currentFps;

    public long GetCurrentBandwidth() => _currentBandwidth;

    public long GetAverageLatency() => _averageLatency;

    public void Reset()
    {
        ResetCalled = true;
        RecordFrameSentCallCount = 0;
    }

    public void SetRecommendedQuality(int quality)
    {
        _recommendedQuality = quality;
    }

    public void SetCurrentFps(double fps)
    {
        _currentFps = fps;
    }

    public void SetCurrentBandwidth(long bandwidth)
    {
        _currentBandwidth = bandwidth;
    }

    public void SetAverageLatency(long latencyMs)
    {
        _averageLatency = latencyMs;
    }
}

/// <summary>In-memory fake for <see cref="IClipboardService"/> used in host tests.</summary>
internal sealed class FakeClipboardService : IClipboardService
{
    public bool IsMonitoring { get; private set; }
    public int StartCallCount { get; private set; }
    public int StopCallCount { get; private set; }

    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        IsMonitoring = true;
        StartCallCount++;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        IsMonitoring = false;
        StopCallCount++;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<byte[]?> GetImageAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<byte[]?>(null);

    public Task SetImageAsync(byte[] pngData, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>In-memory fake for <see cref="IAudioCaptureService"/> used in host tests.</summary>
internal sealed class FakeAudioCaptureService : IAudioCaptureService
{
    public bool IsCapturing { get; private set; }
    public int StartCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public AudioCaptureSettings Settings { get; private set; } = new AudioCaptureSettings();

    public event EventHandler<AudioData>? AudioCaptured;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        IsCapturing = true;
        StartCallCount++;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsCapturing = false;
        StopCallCount++;
        return Task.CompletedTask;
    }

    public void UpdateSettings(AudioCaptureSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }
}

/// <summary>In-memory fake for <see cref="IMessagingService"/> used in host tests.</summary>
internal sealed class FakeMessagingService : IMessagingService
{
    private readonly List<ChatMessage> _messages = new();

    public int UnreadCount => _messages.Count(m => !m.IsRead);

    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<string>? MessageRead;

    public void Initialize(string deviceId, string deviceName)
    {
        // No-op for tests
    }

    public Task<ChatMessage> SendMessageAsync(string text, string? messageType = null)
    {
        var message = new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Text = text,
            MessageType = messageType
        };
        _messages.Add(message);
        return Task.FromResult(message);
    }

    public Task MarkAsReadAsync(string messageId)
    {
        var message = _messages.FirstOrDefault(m => m.MessageId == messageId);
        if (message != null)
        {
            message.IsRead = true;
        }
        return Task.CompletedTask;
    }

    public IReadOnlyList<ChatMessage> GetMessages()
    {
        return _messages.AsReadOnly();
    }

    public void ClearMessages()
    {
        _messages.Clear();
    }
}

/// <summary>In-memory fake for <see cref="ISessionRecorder"/> used in host tests.</summary>
internal sealed class FakeSessionRecorder : ISessionRecorder
{
    public bool IsRecording { get; private set; }
    public bool IsPaused { get; private set; }
    public string? CurrentFilePath { get; private set; }
    public TimeSpan RecordedDuration { get; private set; }

    public int StartRecordingCallCount { get; private set; }
    public int StopRecordingCallCount { get; private set; }
    public int WriteFrameCallCount { get; private set; }
    public int WriteAudioCallCount { get; private set; }

    public event EventHandler<string>? RecordingStarted;
    public event EventHandler<string>? RecordingStopped;
    public event EventHandler<string>? RecordingError;

    public Task<bool> StartRecordingAsync(string filePath, int frameRate = 15, bool includeAudio = true, CancellationToken cancellationToken = default)
    {
        if (IsRecording) return Task.FromResult(false);

        IsRecording = true;
        CurrentFilePath = filePath;
        StartRecordingCallCount++;
        RecordingStarted?.Invoke(this, filePath);
        return Task.FromResult(true);
    }

    public Task<bool> StopRecordingAsync()
    {
        if (!IsRecording) return Task.FromResult(false);

        IsRecording = false;
        var path = CurrentFilePath;
        CurrentFilePath = null;
        StopRecordingCallCount++;
        RecordingStopped?.Invoke(this, path ?? "unknown");
        return Task.FromResult(true);
    }

    public Task<bool> PauseRecordingAsync()
    {
        if (!IsRecording || IsPaused) return Task.FromResult(false);
        IsPaused = true;
        return Task.FromResult(true);
    }

    public Task<bool> ResumeRecordingAsync()
    {
        if (!IsRecording || !IsPaused) return Task.FromResult(false);
        IsPaused = false;
        return Task.FromResult(true);
    }

    public Task WriteFrameAsync(ScreenData screenData)
    {
        if (IsRecording && !IsPaused)
            WriteFrameCallCount++;
        return Task.CompletedTask;
    }

    public Task WriteAudioAsync(AudioData audioData)
    {
        if (IsRecording && !IsPaused)
            WriteAudioCallCount++;
        return Task.CompletedTask;
    }
}

// ── Tests ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for <see cref="RemoteDesktopHost"/> covering:
/// <list type="bullet">
///   <item><description>2.3 — Screen streaming (host → client): capture gating, frame forwarding, stop-on-disconnect.</description></item>
///   <item><description>2.5 — Remote input relay (client → host): relay when paired, ignore when not.</description></item>
/// </list>
/// </summary>
public class RemoteDesktopHostTests : IAsyncDisposable
{
    private readonly FakeCommunicationService _comm = new();
    private readonly FakeScreenCapture _screen = new();
    private readonly FakeInputHandler _input = new();
    private readonly FakePairingService _pairing = new();
    private readonly FakeNetworkDiscovery _discovery = new();
    private readonly FakeSessionManager _sessionManager = new();
    private readonly FakeDeltaFrameEncoder _deltaEncoder = new();
    private readonly FakePerformanceMonitor _perfMonitor = new();
    private readonly FakeClipboardService _clipboard = new();
    private readonly FakeAudioCaptureService _audioCapture = new();
    private readonly FakeSessionRecorder _recorder = new();
    private readonly FakeMessagingService _messaging = new();
    private readonly FakeConnectionRequestNotificationPublisher _notificationPublisher = new();
    private readonly FakeUserAccountService _userAccount = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly RemoteDesktopHost _host;
    private Task? _hostTask;

    public RemoteDesktopHostTests()
    {
        _host = new RemoteDesktopHost(
            NullLogger<RemoteDesktopHost>.Instance,
            _discovery,
            _screen,
            _input,
            _comm,
            _pairing,
            _sessionManager,
            _deltaEncoder,
            _perfMonitor,
            _clipboard,
            _audioCapture,
            _recorder,
            _messaging,
            _notificationPublisher,
            userAccountService: _userAccount);
    }

    /// <summary>Kick off the host as a BackgroundService and wait for it to initialize.</summary>
    private async Task StartHostAsync()
    {
        _hostTask = _host.StartAsync(_cts.Token);
        await Task.Delay(60); // let ExecuteAsync reach its idle loop
    }

    /// <summary>
    /// Full happy-path pairing sequence:
    /// 1. Connection state → connected
    /// 2. PairingRequest with the correct PIN
    /// 3. Wait for async Task.Run inside OnPairingRequestReceived to finish
    /// </summary>
    private async Task SimulatePairingAsync()
    {
        _comm.RaiseConnectionStateChanged(connected: true);
        await Task.Delay(20);

        _comm.RaisePairingRequest(new PairingRequest
        {
            ClientDeviceId = "test-client",
            ClientDeviceName = "TestDevice",
            Pin = _pairing.CurrentPin!,
            RequestedAt = DateTime.UtcNow
        });

        // Allow the Task.Run inside OnPairingRequestReceived to complete
        await Task.Delay(120);
    }

    // ── Feature 2.3: Screen streaming (host → client) ─────────────────────────

    [Fact]
    public async Task ScreenCapture_NotStarted_BeforeAnyClientConnects()
    {
        await StartHostAsync();

        Assert.Equal(0, _screen.StartCallCount);
        Assert.False(_screen.IsCapturing);
    }

    [Fact]
    public async Task ScreenCapture_NotStarted_WhenClientConnectsButBeforePairing()
    {
        await StartHostAsync();

        _comm.RaiseConnectionStateChanged(connected: true);
        await Task.Delay(60);

        // Streaming must NOT begin until the client successfully pairs
        Assert.Equal(0, _screen.StartCallCount);
    }

    [Fact]
    public async Task ScreenCapture_StartsAfterSuccessfulPairing()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        Assert.Equal(1, _screen.StartCallCount);
        Assert.True(_screen.IsCapturing);
    }

    [Fact]
    public async Task ScreenCapture_NotStarted_WhenPairingFails()
    {
        _pairing.ValidatePinResult = false;

        await StartHostAsync();
        _comm.RaiseConnectionStateChanged(connected: true);
        _comm.RaisePairingRequest(new PairingRequest
        {
            ClientDeviceId = "bad-client",
            Pin = "000000",
            RequestedAt = DateTime.UtcNow
        });
        await Task.Delay(120);

        Assert.Equal(0, _screen.StartCallCount);
    }

    [Fact]
    public async Task Frame_IsSent_ToClient_AfterSuccessfulPairing()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        var frame = new ScreenData
        {
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.Raw,
            ImageData = new byte[] { 10, 20, 30 }
        };

        _screen.RaiseFrameCaptured(frame);
        await Task.Delay(120); // let the async Task.Run in OnFrameCaptured complete

        Assert.Single(_comm.SentScreenData);
        Assert.Equal(1920, _comm.SentScreenData[0].Width);
        Assert.Equal(1080, _comm.SentScreenData[0].Height);
        Assert.Equal(new byte[] { 10, 20, 30 }, _comm.SentScreenData[0].ImageData);
    }

    [Fact]
    public async Task Frame_NotSent_WhenClientHasNotPaired()
    {
        // Host started, but client never connects or pairs — FrameCaptured handler is never attached
        await StartHostAsync();

        _screen.RaiseFrameCaptured(new ScreenData { Width = 800, Height = 600 });
        await Task.Delay(120);

        Assert.Empty(_comm.SentScreenData);
    }

    [Fact]
    public async Task Frame_NotSent_WhenIsConnectedIsFalse_EvenIfPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        // Simulate the network connection dropping without a state-changed event
        _comm.IsConnected = false;

        _screen.RaiseFrameCaptured(new ScreenData { Width = 800, Height = 600 });
        await Task.Delay(120);

        Assert.Empty(_comm.SentScreenData);
    }

    [Fact]
    public async Task MultipleFrames_AllSent_ToClient()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        for (int i = 1; i <= 5; i++)
        {
            _screen.RaiseFrameCaptured(new ScreenData
            {
                Width = i * 100,
                Height = i * 75,
                ImageData = new byte[] { (byte)i }
            });
        }
        await Task.Delay(300); // allow all 5 async Task.Run sends to land

        // All 5 frames must be delivered; concurrent Task.Run sends don't
        // guarantee insertion order, so check presence rather than sequence.
        Assert.Equal(5, _comm.SentScreenData.Count);
        var widths = _comm.SentScreenData.Select(s => s.Width).ToHashSet();
        for (int i = 1; i <= 5; i++)
            Assert.Contains(i * 100, widths);
    }

    [Fact]
    public async Task Frames_AreThrottled_WhenConnectionBandwidthDrops()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        _perfMonitor.SetCurrentBandwidth(600_000);
        _perfMonitor.SetAverageLatency(220);

        for (int i = 0; i < 5; i++)
        {
            _screen.RaiseFrameCaptured(new ScreenData
            {
                Width = 1280,
                Height = 720,
                Format = ScreenDataFormat.Raw,
                ImageData = new byte[] { 1, 2, 3, (byte)i }
            });
        }

        await Task.Delay(150);

        Assert.True(_comm.SentScreenData.Count < 5,
            $"Expected throttling to drop frames, but {_comm.SentScreenData.Count} were sent.");
    }

    [Fact]
    public async Task ScreenQuality_IsReduced_WhenConnectionBandwidthDrops()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        _perfMonitor.SetRecommendedQuality(85);
        _perfMonitor.SetCurrentBandwidth(700_000);
        _perfMonitor.SetAverageLatency(180);

        _screen.RaiseFrameCaptured(new ScreenData
        {
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.Raw,
            ImageData = new byte[] { 9, 8, 7, 6 }
        });

        await Task.Delay(120);

        Assert.True(_screen.LastQuality <= 60,
            $"Expected reduced capture quality under constrained bandwidth, but got {_screen.LastQuality}.");
    }

    [Fact]
    public async Task ScreenCapture_StopsWhenClientDisconnects()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        Assert.Equal(1, _screen.StartCallCount);

        // Simulate disconnect
        _comm.RaiseConnectionStateChanged(connected: false);
        await Task.Delay(60);

        Assert.Equal(1, _screen.StopCallCount);
        Assert.False(_screen.IsCapturing);
    }

    [Fact]
    public async Task Frame_NotSent_AfterClientDisconnects()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        _comm.RaiseConnectionStateChanged(connected: false);
        await Task.Delay(60);

        // Any frames that fire after disconnect should be silently dropped
        _screen.RaiseFrameCaptured(new ScreenData { Width = 1280, Height = 720 });
        await Task.Delay(120);

        Assert.Empty(_comm.SentScreenData);
    }

    // ── Pairing response assertions ───────────────────────────────────────────

    [Fact]
    public async Task PairingResponse_Sent_WithSuccess_OnValidPin()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        Assert.Single(_comm.SentPairingResponses);
        var response = _comm.SentPairingResponses[0];
        Assert.True(response.Success);
        Assert.NotNull(response.SessionToken);
        Assert.False(string.IsNullOrEmpty(response.SessionToken));
    }

    [Fact]
    public async Task TrustedDevice_PairsWithoutPinAndBypassesPinValidation()
    {
        _userAccount.SignIn();
        _userAccount.TrustDevice("trusted-mobile", "123 456 789");
        _pairing.ValidatePinResult = false;

        await StartHostAsync();
        _comm.RaiseConnectionStateChanged(connected: true);
        _comm.RaisePairingRequest(new PairingRequest
        {
            ClientDeviceId = "trusted-mobile",
            ClientInternetDeviceId = "123 456 789",
            ClientDeviceName = "Trusted Phone",
            Pin = string.Empty,
            RequestedAt = DateTime.UtcNow
        });
        await Task.Delay(120);

        var response = Assert.Single(_comm.SentPairingResponses);
        Assert.True(response.Success);
        Assert.Contains("Trusted device", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _pairing.ValidatePinCallCount);
        Assert.True(_screen.IsCapturing);
    }

    [Fact]
    public async Task SuccessfulPairing_RegistersClientDeviceForSignedInAccount()
    {
        _userAccount.SignIn();

        await StartHostAsync();
        await SimulatePairingAsync();

        var device = Assert.Single(_userAccount.ManagedDevices);
        Assert.Equal("test-client", device.DeviceId);
        Assert.Equal("TestDevice", device.DeviceName);
    }

    [Fact]
    public async Task PairingResponse_Sent_WithInvalidPin_OnBadPin()
    {
        _pairing.ValidatePinResult = false;

        await StartHostAsync();
        _comm.RaiseConnectionStateChanged(connected: true);
        _comm.RaisePairingRequest(new PairingRequest
        {
            ClientDeviceId = "bad",
            Pin = "000000",
            RequestedAt = DateTime.UtcNow
        });
        await Task.Delay(120);

        Assert.Single(_comm.SentPairingResponses);
        var response = _comm.SentPairingResponses[0];
        Assert.False(response.Success);
        Assert.Equal(PairingFailureReason.InvalidPin, response.FailureReason);
    }

    [Fact]
    public async Task OnPairingRequestReceived_PublishesIncomingConnectionNotification()
    {
        await StartHostAsync();

        _comm.RaisePairingRequest(new PairingRequest
        {
            ClientDeviceId = "mobile-device-1",
            ClientDeviceName = "Test Phone",
            Pin = _pairing.CurrentPin,
            RequestedAt = DateTime.UtcNow
        });

        await Task.Delay(100);

        var alert = Assert.Single(_notificationPublisher.Alerts);
        Assert.Equal(Environment.MachineName, alert.HostDeviceName);
        Assert.Equal("mobile-device-1", alert.ClientDeviceId);
        Assert.Equal("Test Phone", alert.ClientDeviceName);
    }

    [Fact]
    public async Task PairingResponse_Sent_WithTooManyAttempts_WhenLockedOut()
    {
        _pairing.ValidatePinResult = false;
        _pairing.IsLockedOut = true;

        await StartHostAsync();
        _comm.RaiseConnectionStateChanged(connected: true);
        _comm.RaisePairingRequest(new PairingRequest
        {
            ClientDeviceId = "bad",
            Pin = "000000",
            RequestedAt = DateTime.UtcNow
        });
        await Task.Delay(120);

        Assert.Single(_comm.SentPairingResponses);
        var response = _comm.SentPairingResponses[0];
        Assert.False(response.Success);
        Assert.Equal(PairingFailureReason.TooManyAttempts, response.FailureReason);
    }

    // ── Feature 2.5: Remote input relay (client → host) ───────────────────────

    [Fact]
    public async Task InputEvent_Relayed_ToInputHandler_WhenPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        var evt = new InputEvent
        {
            Type = InputEventType.MouseClick,
            X = 200,
            Y = 300,
            IsPressed = true
        };

        _comm.RaiseInputEventReceived(evt);
        await Task.Delay(120);

        Assert.Single(_input.ReceivedEvents);
        Assert.Equal(InputEventType.MouseClick, _input.ReceivedEvents[0].Type);
        Assert.Equal(200, _input.ReceivedEvents[0].X);
        Assert.Equal(300, _input.ReceivedEvents[0].Y);
        Assert.True(_input.ReceivedEvents[0].IsPressed);
    }

    [Fact]
    public async Task InputEvent_NotRelayed_WhenClientHasNotPaired()
    {
        await StartHostAsync();
        // No pairing — _clientPaired remains false

        _comm.RaiseInputEventReceived(new InputEvent { Type = InputEventType.KeyPress, KeyCode = "A" });
        await Task.Delay(120);

        Assert.Empty(_input.ReceivedEvents);
    }

    [Fact]
    public async Task MultipleInputEvents_AllRelayed_WhenPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        var events = new[]
        {
            new InputEvent { Type = InputEventType.MouseMove,  X = 10, Y = 20 },
            new InputEvent { Type = InputEventType.MouseClick, X = 50, Y = 60, IsPressed = true },
            new InputEvent { Type = InputEventType.KeyPress,   KeyCode = "Space" },
            new InputEvent { Type = InputEventType.MouseWheel, Y = 3 },
            new InputEvent { Type = InputEventType.TextInput,  Text = "hello" }
        };

        foreach (var e in events)
            _comm.RaiseInputEventReceived(e);

        await Task.Delay(250);

        Assert.Equal(5, _input.ReceivedEvents.Count);
    }

    [Fact]
    public async Task InputEvent_NotRelayed_AfterClientDisconnects()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        // Disconnect — resets _clientPaired to false
        _comm.RaiseConnectionStateChanged(connected: false);
        await Task.Delay(60);

        _comm.RaiseInputEventReceived(new InputEvent { Type = InputEventType.KeyPress, KeyCode = "A" });
        await Task.Delay(120);

        // No events should have been relayed after disconnect
        Assert.Empty(_input.ReceivedEvents);
    }

    [Fact]
    public async Task AllInputEventTypes_Relayed_WhenPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        var allTypes = new[]
        {
            InputEventType.MouseMove,
            InputEventType.MouseClick,
            InputEventType.MouseWheel,
            InputEventType.KeyPress,
            InputEventType.KeyRelease,
            InputEventType.TextInput
        };

        foreach (var type in allTypes)
            _comm.RaiseInputEventReceived(new InputEvent { Type = type });

        await Task.Delay(300);

        Assert.Equal(allTypes.Length, _input.ReceivedEvents.Count);

        // Order is not guaranteed (events are dispatched on a background Task.Run);
        // verify all expected types are present.
        var receivedTypes = _input.ReceivedEvents.Select(e => e.Type).ToHashSet();
        foreach (var type in allTypes)
            Assert.Contains(type, receivedTypes);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InputHandler_IsStarted_WhenHostStarts()
    {
        await StartHostAsync();
        Assert.True(_input.IsActive);
    }

    [Fact]
    public async Task InputHandler_IsStopped_WhenHostStops()
    {
        await StartHostAsync();
        Assert.True(_input.IsActive);

        // Cancel the hosted service's lifetime token, then call StopAsync so the
        // test waits for ExecuteAsync's finally-block (which calls StopAsync on
        // all dependencies) before asserting.  BackgroundService.StartAsync returns
        // Task.CompletedTask when ExecuteAsync is long-running, so we cannot rely
        // on _hostTask for shutdown synchronisation.
        _cts.Cancel();
        await _host.StopAsync(CancellationToken.None);

        Assert.False(_input.IsActive);
    }

    [Fact]
    public async Task Host_DoesNotThrow_OnCleanCancellation()
    {
        await StartHostAsync();

        _cts.Cancel();
        // StopAsync must complete without throwing
        var ex = await Record.ExceptionAsync(() => _host.StopAsync(CancellationToken.None));
        Assert.Null(ex);
    }

    // ── Feature 3.5: Connection quality indicator ─────────────────────────────

    [Fact]
    public async Task ConnectionQuality_NotSent_BeforePairing()
    {
        await StartHostAsync();

        _comm.RaiseConnectionStateChanged(connected: true);
        await Task.Delay(3000); // Wait more than 2 seconds (quality update interval)

        // Should not send quality updates before pairing
        Assert.Empty(_comm.SentConnectionQuality);
    }

    [Fact]
    public async Task ConnectionQuality_SentPeriodically_AfterPairing()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        // Wait for multiple quality update intervals (2 seconds each)
        await Task.Delay(5000);

        // Should have sent at least 2 quality updates (at ~2s and ~4s)
        Assert.True(_comm.SentConnectionQuality.Count >= 2,
            $"Expected at least 2 quality updates, got {_comm.SentConnectionQuality.Count}");
    }

    [Fact]
    public async Task ConnectionQuality_ContainsValidMetrics_WhenSent()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        // Wait for at least one quality update
        await Task.Delay(3000);

        Assert.NotEmpty(_comm.SentConnectionQuality);
        var quality = _comm.SentConnectionQuality[0];

        // Verify all metrics are present and valid
        Assert.True(quality.Fps >= 0);
        Assert.True(quality.Bandwidth >= 0);
        Assert.True(quality.Latency >= 0);
        Assert.NotEqual(DateTime.MinValue, quality.Timestamp);
        Assert.True(Enum.IsDefined(typeof(QualityRating), quality.Rating));
    }

    [Fact]
    public async Task ConnectionQuality_StopsSending_AfterDisconnect()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        // Wait for initial quality updates
        await Task.Delay(3000);
        int countBeforeDisconnect = _comm.SentConnectionQuality.Count;

        // Disconnect
        _comm.RaiseConnectionStateChanged(connected: false);

        // Wait another quality interval
        await Task.Delay(3000);

        // Should not have sent more quality updates after disconnect
        Assert.Equal(countBeforeDisconnect, _comm.SentConnectionQuality.Count);
    }

    // ── Feature 6.8: Session toolbar controls ─────────────────────────────────

    [Fact]
    public async Task SessionControl_GetMonitors_ReturnsAvailableMonitors_WhenPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        _comm.RaiseSessionControlRequest(new SessionControlRequest
        {
            RequestId = "req-monitors",
            Command = SessionControlCommand.GetMonitors
        });

        await Task.Delay(120);

        Assert.Single(_comm.SentSessionControlResponses);
        var response = _comm.SentSessionControlResponses[0];
        Assert.True(response.Success);
        Assert.NotNull(response.Monitors);
        Assert.Single(response.Monitors!);
        Assert.Equal("fake-monitor", response.Monitors[0].Id);
    }

    [Fact]
    public async Task SessionControl_SelectMonitor_UpdatesCaptureAndResetsDeltaEncoder_WhenPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        _comm.RaiseSessionControlRequest(new SessionControlRequest
        {
            RequestId = "req-select-monitor",
            Command = SessionControlCommand.SelectMonitor,
            MonitorId = "fake-monitor"
        });

        await Task.Delay(120);

        Assert.Equal("fake-monitor", _screen.SelectedMonitorId);
        Assert.True(_deltaEncoder.ResetCalled);
        Assert.Single(_comm.SentSessionControlResponses);
        Assert.True(_comm.SentSessionControlResponses[0].Success);
        Assert.Equal("fake-monitor", _comm.SentSessionControlResponses[0].SelectedMonitorId);
    }

    [Fact]
    public async Task SessionControl_SetQuality_UpdatesScreenCapture_WhenPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        _comm.RaiseSessionControlRequest(new SessionControlRequest
        {
            RequestId = "req-quality",
            Command = SessionControlCommand.SetQuality,
            Quality = 65
        });

        await Task.Delay(120);

        Assert.Equal(65, _screen.LastQuality);
        Assert.Single(_comm.SentSessionControlResponses);
        Assert.True(_comm.SentSessionControlResponses[0].Success);
        Assert.Equal(65, _comm.SentSessionControlResponses[0].AppliedQuality);
    }

    [Fact]
    public async Task SessionControl_SetImageFormat_EncodesSubsequentFrames_WhenPaired()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await StartHostAsync();
        await SimulatePairingAsync();

        _comm.RaiseSessionControlRequest(new SessionControlRequest
        {
            RequestId = "req-format",
            Command = SessionControlCommand.SetImageFormat,
            ImageFormat = ScreenDataFormat.JPEG
        });

        await Task.Delay(120);

        _screen.RaiseFrameCaptured(new ScreenData
        {
            Width = 1,
            Height = 1,
            Format = ScreenDataFormat.Raw,
            Quality = 75,
            ImageData = new byte[] { 0, 0, 0, 255 }
        });

        await Task.Delay(120);

        Assert.True(_comm.SentSessionControlResponses[0].Success);
        Assert.Equal(ScreenDataFormat.JPEG, _comm.SentSessionControlResponses[0].AppliedImageFormat);
        Assert.NotEmpty(_comm.SentScreenData);
        Assert.Equal(ScreenDataFormat.JPEG, _comm.SentScreenData[^1].Format);
    }

    [Fact]
    public async Task SessionControl_SetAudioEnabled_StopsAudioCapture_WhenPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        Assert.True(_audioCapture.IsCapturing);

        _comm.RaiseSessionControlRequest(new SessionControlRequest
        {
            RequestId = "req-audio-off",
            Command = SessionControlCommand.SetAudioEnabled,
            AudioEnabled = false
        });

        await Task.Delay(120);

        Assert.False(_audioCapture.IsCapturing);
        Assert.Equal(1, _audioCapture.StopCallCount);
        Assert.Equal(SessionControlCommand.SetAudioEnabled, _comm.SentSessionControlResponses[^1].Command);
        Assert.False(_comm.SentSessionControlResponses[^1].AppliedAudioEnabled);
    }

    [Fact]
    public async Task SessionControl_IgnoredWithFailureResponse_WhenClientNotPaired()
    {
        await StartHostAsync();

        _comm.RaiseSessionControlRequest(new SessionControlRequest
        {
            RequestId = "req-unpaired",
            Command = SessionControlCommand.SetQuality,
            Quality = 50
        });

        await Task.Delay(120);

        Assert.Single(_comm.SentSessionControlResponses);
        Assert.False(_comm.SentSessionControlResponses[0].Success);
        Assert.Equal("Client is not paired.", _comm.SentSessionControlResponses[0].ErrorMessage);
    }

    // ── IAsyncDisposable ───────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();

        // StopAsync waits for ExecuteAsync to finish (including its finally block),
        // which is what we need to drain background Task.Run work before teardown.
        try { await _host.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { /* ignore */ }

        _cts.Dispose();
        _comm.Dispose();
    }
}
