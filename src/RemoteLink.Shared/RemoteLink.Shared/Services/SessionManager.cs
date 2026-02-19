using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="ISessionManager"/>.
///
/// Lifecycle state machine (per session):
/// <code>
///   [Created] ──OnConnected──→ Connected
///   Connected ──OnDisconnected──→ Disconnected
///   Disconnected ──TryReconnect (ok)──→ Pending
///   Disconnected ──TryReconnect (exhausted)──→ Error
///   Pending ──OnConnected──→ Connected      (re-entry after reconnect)
///   Any ──EndSession──→ Ended
/// </code>
/// </summary>
public sealed class SessionManager : ISessionManager
{
    // ── Events ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public event EventHandler<RemoteSession>? SessionCreated;

    /// <inheritdoc/>
    public event EventHandler<RemoteSession>? SessionConnected;

    /// <inheritdoc/>
    public event EventHandler<RemoteSession>? SessionDisconnected;

    /// <inheritdoc/>
    public event EventHandler<RemoteSession>? SessionEnded;

    /// <inheritdoc/>
    public event EventHandler<RemoteSession>? ReconnectFailed;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<string, RemoteSession> _sessions = new();
    private readonly object _lock = new();

    /// <summary>
    /// Overridable clock — swap in tests to control DateTime.UtcNow.
    /// Defaults to <see cref="DateTime.UtcNow"/>.
    /// </summary>
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public RemoteSession CreateSession(
        string hostId,
        string hostDeviceName,
        string clientId,
        string clientDeviceName)
    {
        var utcNow = UtcNow;   // capture current delegate so session shares the same clock
        var session = new RemoteSession
        {
            SessionId        = Guid.NewGuid().ToString(),
            HostId           = hostId,
            HostDeviceName   = hostDeviceName,
            ClientId         = clientId,
            ClientDeviceName = clientDeviceName,
            CreatedAt        = utcNow(),
            Status           = SessionStatus.Pending,
            ClockFunc        = utcNow
        };

        lock (_lock)
            _sessions[session.SessionId] = session;

        SessionCreated?.Invoke(this, session);
        return session;
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public RemoteSession? GetSession(string sessionId)
    {
        lock (_lock)
            return _sessions.TryGetValue(sessionId, out var s) ? s : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<RemoteSession> GetAllSessions()
    {
        lock (_lock)
            return _sessions.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public RemoteSession? GetActiveSession()
    {
        lock (_lock)
            return _sessions.Values.FirstOrDefault(s => s.Status == SessionStatus.Connected);
    }

    // ── Lifecycle transitions ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public void OnConnected(string sessionId)
    {
        RemoteSession session;

        lock (_lock)
        {
            session = RequireSession(sessionId);

            if (session.Status is SessionStatus.Error or SessionStatus.Ended)
                throw new InvalidOperationException(
                    $"Cannot connect session '{sessionId}' in terminal state {session.Status}.");

            session.LastConnectedAt    = UtcNow();
            session.ReconnectAttempts  = 0;
            session.DisconnectReason   = null;
            session.Status             = SessionStatus.Connected;
        }

        SessionConnected?.Invoke(this, session);
    }

    /// <inheritdoc/>
    public void OnDisconnected(string sessionId, string? reason = null)
    {
        RemoteSession session;

        lock (_lock)
        {
            session = RequireSession(sessionId);

            // Accumulate duration from the last connect time
            if (session.LastConnectedAt.HasValue && session.Status == SessionStatus.Connected)
            {
                var now = UtcNow();
                session._accumulatedDuration += now - session.LastConnectedAt.Value;
                session.DisconnectedAt = now;
            }
            else
            {
                session.DisconnectedAt = UtcNow();
            }

            session.DisconnectReason = reason;
            session.Status           = SessionStatus.Disconnected;
        }

        SessionDisconnected?.Invoke(this, session);
    }

    /// <inheritdoc/>
    public bool TryReconnect(string sessionId)
    {
        RemoteSession session;
        bool accepted;

        lock (_lock)
        {
            session = RequireSession(sessionId);

            session.ReconnectAttempts++;

            if (session.ReconnectAttempts > session.MaxReconnectAttempts)
            {
                session.Status = SessionStatus.Error;
                accepted = false;
            }
            else
            {
                session.Status = SessionStatus.Pending;
                accepted = true;
            }
        }

        if (!accepted)
            ReconnectFailed?.Invoke(this, session);

        return accepted;
    }

    /// <inheritdoc/>
    public void EndSession(string sessionId)
    {
        RemoteSession session;

        lock (_lock)
        {
            session = RequireSession(sessionId);

            // If we were connected, close out the duration
            if (session.Status == SessionStatus.Connected && session.LastConnectedAt.HasValue)
            {
                var now = UtcNow();
                session._accumulatedDuration += now - session.LastConnectedAt.Value;
                session.DisconnectedAt = now;
            }

            session.Status = SessionStatus.Ended;
        }

        SessionEnded?.Invoke(this, session);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the session for <paramref name="sessionId"/>, throwing
    /// <see cref="KeyNotFoundException"/> when not found.  Must be called
    /// inside the <see cref="_lock"/>.
    /// </summary>
    private RemoteSession RequireSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");
        return session;
    }
}
