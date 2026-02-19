using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Manages the lifecycle of <see cref="RemoteSession"/> objects: creation,
/// state transitions (connect / disconnect / reconnect / end), and history.
/// </summary>
public interface ISessionManager
{
    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new session in <see cref="SessionStatus.Pending"/> state and
    /// fires <see cref="SessionCreated"/>.
    /// </summary>
    /// <param name="hostId">Device ID of the desktop host.</param>
    /// <param name="hostDeviceName">Human-readable host name.</param>
    /// <param name="clientId">Device ID of the connecting client.</param>
    /// <param name="clientDeviceName">Human-readable client name.</param>
    /// <returns>The newly created <see cref="RemoteSession"/>.</returns>
    RemoteSession CreateSession(
        string hostId,
        string hostDeviceName,
        string clientId,
        string clientDeviceName);

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves a session by its ID, or <c>null</c> if not found.
    /// </summary>
    RemoteSession? GetSession(string sessionId);

    /// <summary>
    /// Returns all sessions tracked by this manager (in any state).
    /// </summary>
    IReadOnlyList<RemoteSession> GetAllSessions();

    /// <summary>
    /// Returns the first session in <see cref="SessionStatus.Connected"/> state,
    /// or <c>null</c> if no session is currently connected.
    /// </summary>
    RemoteSession? GetActiveSession();

    // ── Lifecycle transitions ─────────────────────────────────────────────────

    /// <summary>
    /// Transitions <paramref name="sessionId"/> from Pending/Disconnected to
    /// <see cref="SessionStatus.Connected"/>, records <c>LastConnectedAt</c>,
    /// resets <c>ReconnectAttempts</c> to 0, and fires <see cref="SessionConnected"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the session is already in a terminal state
    /// (<see cref="SessionStatus.Error"/> or <see cref="SessionStatus.Ended"/>).
    /// </exception>
    void OnConnected(string sessionId);

    /// <summary>
    /// Transitions <paramref name="sessionId"/> to
    /// <see cref="SessionStatus.Disconnected"/>, records <c>DisconnectedAt</c>
    /// and an optional <paramref name="reason"/>, and fires
    /// <see cref="SessionDisconnected"/>.
    /// </summary>
    void OnDisconnected(string sessionId, string? reason = null);

    /// <summary>
    /// Attempts to reconnect a <see cref="SessionStatus.Disconnected"/> session.
    /// Increments <c>ReconnectAttempts</c> and sets the session back to
    /// <see cref="SessionStatus.Pending"/> so the caller can re-initiate the
    /// TCP/pairing flow.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the attempt is accepted (below
    /// <see cref="RemoteSession.MaxReconnectAttempts"/>);
    /// <c>false</c> when the limit is exceeded — in that case the session
    /// transitions to <see cref="SessionStatus.Error"/> and
    /// <see cref="ReconnectFailed"/> is fired.
    /// </returns>
    bool TryReconnect(string sessionId);

    /// <summary>
    /// Gracefully terminates <paramref name="sessionId"/>, setting its status
    /// to <see cref="SessionStatus.Ended"/> and firing <see cref="SessionEnded"/>.
    /// </summary>
    void EndSession(string sessionId);

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when a new session is created.</summary>
    event EventHandler<RemoteSession> SessionCreated;

    /// <summary>Fired when a session transitions to Connected.</summary>
    event EventHandler<RemoteSession> SessionConnected;

    /// <summary>Fired when a session transitions to Disconnected.</summary>
    event EventHandler<RemoteSession> SessionDisconnected;

    /// <summary>Fired when a session is gracefully ended.</summary>
    event EventHandler<RemoteSession> SessionEnded;

    /// <summary>
    /// Fired when <see cref="TryReconnect"/> is called on a session that has
    /// exhausted its <see cref="RemoteSession.MaxReconnectAttempts"/>.
    /// </summary>
    event EventHandler<RemoteSession> ReconnectFailed;
}
