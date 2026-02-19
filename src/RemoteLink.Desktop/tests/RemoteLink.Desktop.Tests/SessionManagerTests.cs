using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Desktop.Tests;

/// <summary>
/// Unit tests for <see cref="SessionManager"/>.
/// A controllable clock is injected so we can verify timing without sleeping.
/// </summary>
public class SessionManagerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SessionManager BuildSut(DateTime? now = null)
    {
        var t = now ?? T0;
        return new SessionManager { UtcNow = () => t };
    }

    private static RemoteSession CreateSession(
        SessionManager sut,
        string? hostId   = "host-1",
        string? hostName = "Desktop",
        string? clientId = "client-1",
        string? clientName = "Mobile")
        => sut.CreateSession(hostId!, hostName!, clientId!, clientName!);

    // ── CreateSession ─────────────────────────────────────────────────────────

    [Fact]
    public void CreateSession_ReturnsSessionWithPendingStatus()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        Assert.Equal(SessionStatus.Pending, session.Status);
    }

    [Fact]
    public void CreateSession_AssignsNonEmptySessionId()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        Assert.False(string.IsNullOrWhiteSpace(session.SessionId));
    }

    [Fact]
    public void CreateSession_AssignsCreatedAtFromClock()
    {
        var sut = BuildSut(T0);
        var session = CreateSession(sut);
        Assert.Equal(T0, session.CreatedAt);
    }

    [Fact]
    public void CreateSession_StoresDeviceIdentifiers()
    {
        var sut = BuildSut();
        var session = sut.CreateSession("h1", "Desktop PC", "c1", "iPhone");
        Assert.Equal("h1",          session.HostId);
        Assert.Equal("Desktop PC",  session.HostDeviceName);
        Assert.Equal("c1",          session.ClientId);
        Assert.Equal("iPhone",      session.ClientDeviceName);
    }

    [Fact]
    public void CreateSession_FiresSessionCreatedEvent()
    {
        var sut = BuildSut();
        RemoteSession? fired = null;
        sut.SessionCreated += (_, s) => fired = s;

        var session = CreateSession(sut);

        Assert.NotNull(fired);
        Assert.Equal(session.SessionId, fired!.SessionId);
    }

    [Fact]
    public void CreateSession_TwoSessions_HaveDistinctIds()
    {
        var sut = BuildSut();
        var s1 = CreateSession(sut);
        var s2 = CreateSession(sut);
        Assert.NotEqual(s1.SessionId, s2.SessionId);
    }

    // ── GetSession / GetAllSessions ───────────────────────────────────────────

    [Fact]
    public void GetSession_ReturnsNullForUnknownId()
    {
        var sut = BuildSut();
        Assert.Null(sut.GetSession("does-not-exist"));
    }

    [Fact]
    public void GetSession_ReturnsSessionById()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        Assert.Equal(session.SessionId, sut.GetSession(session.SessionId)?.SessionId);
    }

    [Fact]
    public void GetAllSessions_EmptyWhenNoSessions()
    {
        var sut = BuildSut();
        Assert.Empty(sut.GetAllSessions());
    }

    [Fact]
    public void GetAllSessions_ReturnsAllCreatedSessions()
    {
        var sut = BuildSut();
        CreateSession(sut);
        CreateSession(sut);
        Assert.Equal(2, sut.GetAllSessions().Count);
    }

    // ── GetActiveSession ──────────────────────────────────────────────────────

    [Fact]
    public void GetActiveSession_ReturnsNullWhenNoneConnected()
    {
        var sut = BuildSut();
        CreateSession(sut);   // Pending, not connected
        Assert.Null(sut.GetActiveSession());
    }

    [Fact]
    public void GetActiveSession_ReturnsConnectedSession()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);

        var active = sut.GetActiveSession();
        Assert.NotNull(active);
        Assert.Equal(session.SessionId, active!.SessionId);
    }

    // ── OnConnected ───────────────────────────────────────────────────────────

    [Fact]
    public void OnConnected_SetsStatusToConnected()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);
        Assert.Equal(SessionStatus.Connected, session.Status);
    }

    [Fact]
    public void OnConnected_SetsLastConnectedAtFromClock()
    {
        var sut = BuildSut(T0);
        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);
        Assert.Equal(T0, session.LastConnectedAt);
    }

    [Fact]
    public void OnConnected_ResetsReconnectAttempts()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        // Simulate one previous reconnect attempt
        sut.OnDisconnected(session.SessionId);
        sut.TryReconnect(session.SessionId);   // attempts → 1
        sut.OnConnected(session.SessionId);    // should reset

        Assert.Equal(0, session.ReconnectAttempts);
    }

    [Fact]
    public void OnConnected_FiresSessionConnectedEvent()
    {
        var sut = BuildSut();
        RemoteSession? fired = null;
        sut.SessionConnected += (_, s) => fired = s;

        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);

        Assert.NotNull(fired);
        Assert.Equal(session.SessionId, fired!.SessionId);
    }

    [Fact]
    public void OnConnected_WhenSessionInErrorState_ThrowsInvalidOperation()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        // Force Error state by exhausting reconnects
        sut.OnDisconnected(session.SessionId);
        for (int i = 0; i <= session.MaxReconnectAttempts; i++)
            sut.TryReconnect(session.SessionId);

        Assert.Throws<InvalidOperationException>(() => sut.OnConnected(session.SessionId));
    }

    [Fact]
    public void OnConnected_WhenSessionInEndedState_ThrowsInvalidOperation()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.EndSession(session.SessionId);

        Assert.Throws<InvalidOperationException>(() => sut.OnConnected(session.SessionId));
    }

    [Fact]
    public void OnConnected_UnknownSessionId_ThrowsKeyNotFound()
    {
        var sut = BuildSut();
        Assert.Throws<KeyNotFoundException>(() => sut.OnConnected("ghost"));
    }

    // ── OnDisconnected ────────────────────────────────────────────────────────

    [Fact]
    public void OnDisconnected_SetsStatusToDisconnected()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);
        sut.OnDisconnected(session.SessionId);
        Assert.Equal(SessionStatus.Disconnected, session.Status);
    }

    [Fact]
    public void OnDisconnected_SetsDisconnectedAtFromClock()
    {
        var t = T0;
        var sut = new SessionManager { UtcNow = () => t };
        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);

        t = T0.AddMinutes(5);
        sut.OnDisconnected(session.SessionId);

        Assert.Equal(t, session.DisconnectedAt);
    }

    [Fact]
    public void OnDisconnected_StoresReason()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);
        sut.OnDisconnected(session.SessionId, "Network timeout");

        Assert.Equal("Network timeout", session.DisconnectReason);
    }

    [Fact]
    public void OnDisconnected_NullReason_IsStored()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);
        sut.OnDisconnected(session.SessionId);

        Assert.Null(session.DisconnectReason);
    }

    [Fact]
    public void OnDisconnected_FiresSessionDisconnectedEvent()
    {
        var sut = BuildSut();
        RemoteSession? fired = null;
        sut.SessionDisconnected += (_, s) => fired = s;

        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);
        sut.OnDisconnected(session.SessionId);

        Assert.NotNull(fired);
        Assert.Equal(session.SessionId, fired!.SessionId);
    }

    // ── Duration accumulation ─────────────────────────────────────────────────

    [Fact]
    public void Duration_WhenConnectedForTwoMinutes_ReturnsCorrectDuration()
    {
        var t = T0;
        var sut = new SessionManager { UtcNow = () => t };
        var session = CreateSession(sut);

        sut.OnConnected(session.SessionId);     // t = T0
        t = T0.AddMinutes(2);
        sut.OnDisconnected(session.SessionId);  // connected for 2 min

        Assert.Equal(TimeSpan.FromMinutes(2), session.Duration);
    }

    [Fact]
    public void Duration_AccumulatesAcrossMultipleConnectCycles()
    {
        var t = T0;
        var sut = new SessionManager { UtcNow = () => t };
        var session = CreateSession(sut);

        // First cycle: 3 min
        sut.OnConnected(session.SessionId);
        t = T0.AddMinutes(3);
        sut.OnDisconnected(session.SessionId);

        // Reconnect, second cycle: 2 min
        sut.TryReconnect(session.SessionId);
        sut.OnConnected(session.SessionId);
        t = T0.AddMinutes(5);
        sut.OnDisconnected(session.SessionId);

        Assert.Equal(TimeSpan.FromMinutes(5), session.Duration);
    }

    [Fact]
    public void Duration_WhileConnected_IncludesCurrentInterval()
    {
        var t = T0;
        var sut = new SessionManager { UtcNow = () => t };
        var session = CreateSession(sut);

        sut.OnConnected(session.SessionId);   // connected at T0
        t = T0.AddSeconds(30);               // advance clock — still connected

        Assert.Equal(TimeSpan.FromSeconds(30), session.Duration);
    }

    [Fact]
    public void Duration_WhenNeverConnected_IsZero()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        Assert.Equal(TimeSpan.Zero, session.Duration);
    }

    // ── TryReconnect ──────────────────────────────────────────────────────────

    [Fact]
    public void TryReconnect_BelowLimit_ReturnsTrue_AndSetsPending()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.OnDisconnected(session.SessionId);

        bool result = sut.TryReconnect(session.SessionId);

        Assert.True(result);
        Assert.Equal(SessionStatus.Pending, session.Status);
    }

    [Fact]
    public void TryReconnect_IncrementsAttemptCount()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.OnDisconnected(session.SessionId);

        sut.TryReconnect(session.SessionId);

        Assert.Equal(1, session.ReconnectAttempts);
    }

    [Fact]
    public void TryReconnect_AtMaxAttempts_ReturnsFalse_AndSetsError()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);   // MaxReconnectAttempts = 3 by default
        sut.OnDisconnected(session.SessionId);

        bool result = false;
        for (int i = 0; i <= session.MaxReconnectAttempts; i++)
            result = sut.TryReconnect(session.SessionId);

        Assert.False(result);
        Assert.Equal(SessionStatus.Error, session.Status);
    }

    [Fact]
    public void TryReconnect_Exhausted_FiresReconnectFailedEvent()
    {
        var sut = BuildSut();
        RemoteSession? fired = null;
        sut.ReconnectFailed += (_, s) => fired = s;

        var session = CreateSession(sut);
        sut.OnDisconnected(session.SessionId);

        for (int i = 0; i <= session.MaxReconnectAttempts; i++)
            sut.TryReconnect(session.SessionId);

        Assert.NotNull(fired);
        Assert.Equal(session.SessionId, fired!.SessionId);
    }

    [Fact]
    public void TryReconnect_ExactlyAtLimit_ReturnsFalse()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.OnDisconnected(session.SessionId);

        // MaxReconnectAttempts = 3: attempts 1,2,3 should succeed; 4th should fail
        for (int i = 0; i < session.MaxReconnectAttempts; i++)
        {
            bool ok = sut.TryReconnect(session.SessionId);
            Assert.True(ok, $"Attempt {i + 1} should be accepted");
            // Re-disconnect to allow the next attempt
            if (i < session.MaxReconnectAttempts - 1)
                sut.OnDisconnected(session.SessionId);
        }

        // The (MaxReconnectAttempts+1)th attempt should fail
        bool final = sut.TryReconnect(session.SessionId);
        Assert.False(final);
    }

    [Fact]
    public void TryReconnect_UnknownSessionId_ThrowsKeyNotFound()
    {
        var sut = BuildSut();
        Assert.Throws<KeyNotFoundException>(() => sut.TryReconnect("ghost"));
    }

    // ── EndSession ────────────────────────────────────────────────────────────

    [Fact]
    public void EndSession_SetsStatusToEnded()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.EndSession(session.SessionId);
        Assert.Equal(SessionStatus.Ended, session.Status);
    }

    [Fact]
    public void EndSession_FiresSessionEndedEvent()
    {
        var sut = BuildSut();
        RemoteSession? fired = null;
        sut.SessionEnded += (_, s) => fired = s;

        var session = CreateSession(sut);
        sut.EndSession(session.SessionId);

        Assert.NotNull(fired);
        Assert.Equal(session.SessionId, fired!.SessionId);
    }

    [Fact]
    public void EndSession_WhenConnected_AccumulatesDuration()
    {
        var t = T0;
        var sut = new SessionManager { UtcNow = () => t };
        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);

        t = T0.AddMinutes(10);
        sut.EndSession(session.SessionId);

        Assert.Equal(TimeSpan.FromMinutes(10), session.Duration);
    }

    [Fact]
    public void EndSession_WhenPending_DoesNotThrow()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        var ex = Record.Exception(() => sut.EndSession(session.SessionId));
        Assert.Null(ex);
    }

    [Fact]
    public void EndSession_UnknownSessionId_ThrowsKeyNotFound()
    {
        var sut = BuildSut();
        Assert.Throws<KeyNotFoundException>(() => sut.EndSession("ghost"));
    }

    // ── GetAllSessions / isolation ────────────────────────────────────────────

    [Fact]
    public void GetAllSessions_IncludesEndedSessions()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.EndSession(session.SessionId);

        Assert.Single(sut.GetAllSessions());
    }

    [Fact]
    public void GetActiveSession_AfterDisconnect_ReturnsNull()
    {
        var sut = BuildSut();
        var session = CreateSession(sut);
        sut.OnConnected(session.SessionId);
        sut.OnDisconnected(session.SessionId);

        Assert.Null(sut.GetActiveSession());
    }

    [Fact]
    public void MultipleSessions_OnlyConnectedOneIsActive()
    {
        var sut = BuildSut();
        var s1 = CreateSession(sut);
        var s2 = CreateSession(sut);

        sut.OnConnected(s1.SessionId);
        // s2 stays Pending

        var active = sut.GetActiveSession();
        Assert.Equal(s1.SessionId, active?.SessionId);
    }
}
