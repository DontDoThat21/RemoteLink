namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents a remote desktop session between a host and a client, including
/// full lifecycle metadata: timing, disconnect info, and reconnect state.
/// </summary>
public class RemoteSession
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Unique identifier for this session, generated at creation time.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Device ID of the desktop host.</summary>
    public string HostId { get; set; } = string.Empty;

    /// <summary>Human-readable name of the desktop host.</summary>
    public string HostDeviceName { get; set; } = string.Empty;

    /// <summary>Device ID of the mobile/remote client.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Human-readable name of the client device.</summary>
    public string ClientDeviceName { get; set; } = string.Empty;

    // ── Timing ────────────────────────────────────────────────────────────────

    /// <summary>UTC timestamp when this session record was created (Pending state).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC timestamp when the session last transitioned to Connected.</summary>
    public DateTime? LastConnectedAt { get; set; }

    /// <summary>UTC timestamp when the session last transitioned to Disconnected.</summary>
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>
    /// Total connected duration.  Computed as the sum of all connected intervals;
    /// if currently connected this includes the current interval up to UtcNow.
    /// </summary>
    public TimeSpan Duration
    {
        get
        {
            // When not connected, _accumulatedDuration already includes all
            // completed intervals (each OnDisconnected/EndSession call adds to it).
            if (Status != SessionStatus.Connected || LastConnectedAt is null)
                return _accumulatedDuration;

            // Session is live — add the current in-progress interval.
            return _accumulatedDuration + (ClockFunc() - LastConnectedAt.Value);
        }
    }

    // Internal: accumulated from previous connect→disconnect cycles.
    internal TimeSpan _accumulatedDuration = TimeSpan.Zero;

    /// <summary>
    /// Injectable clock delegate.  Set by <c>SessionManager</c> at creation
    /// time so that the Duration computation uses the same controllable clock
    /// as the manager (essential for unit testing without Thread.Sleep).
    /// Defaults to <see cref="DateTime.UtcNow"/> when not set.
    /// </summary>
    internal Func<DateTime> ClockFunc { get; set; } = () => DateTime.UtcNow;

    // ── Disconnect info ───────────────────────────────────────────────────────

    /// <summary>Human-readable reason for the last disconnection (null if not yet disconnected).</summary>
    public string? DisconnectReason { get; set; }

    // ── Reconnect state ───────────────────────────────────────────────────────

    /// <summary>
    /// Number of times a reconnection has been attempted after disconnection.
    /// Resets to 0 on a successful connection.
    /// </summary>
    public int ReconnectAttempts { get; set; }

    /// <summary>
    /// Maximum number of reconnect attempts before the session transitions to
    /// <see cref="SessionStatus.Error"/> and is considered dead.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    // ── Status ────────────────────────────────────────────────────────────────

    /// <summary>Current lifecycle status of this session.</summary>
    public SessionStatus Status { get; set; }
}

/// <summary>
/// Lifecycle status of a remote desktop session.
/// </summary>
public enum SessionStatus
{
    /// <summary>Session created; TCP connection or pairing not yet complete.</summary>
    Pending,

    /// <summary>Fully authenticated and streaming.</summary>
    Connected,

    /// <summary>Temporarily disconnected; may be reconnected.</summary>
    Disconnected,

    /// <summary>
    /// Terminal failure — too many reconnect attempts or an unrecoverable error.
    /// The session cannot be recovered.
    /// </summary>
    Error,

    /// <summary>Session gracefully ended by either party.</summary>
    Ended
}
