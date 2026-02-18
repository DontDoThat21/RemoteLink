using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Desktop.Tests;

/// <summary>
/// Unit tests for <see cref="PinPairingService"/>.
/// All tests inject a controllable clock so no real time-based sleeping is needed.
/// </summary>
public class PinPairingServiceTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a service paired with a mutable DateTime so tests can advance time.
    /// Usage:
    /// <code>
    /// var (svc, clock) = CreateServiceWithClock();
    /// clock.Advance(TimeSpan.FromMinutes(6));
    /// </code>
    /// </summary>
    private static (PinPairingService svc, MutableClock clock) CreateServiceWithClock(
        TimeSpan? pinTtl = null, int maxAttempts = 5)
    {
        var clock = new MutableClock();
        var svc = new PinPairingService(
            clock: () => clock.Now,
            pinTtl: pinTtl,
            maxAttempts: maxAttempts);
        return (svc, clock);
    }

    private class MutableClock
    {
        public DateTime Now { get; private set; } = DateTime.UtcNow;
        public void Advance(TimeSpan ts) => Now += ts;
    }

    // ── Interface contract ─────────────────────────────────────────────────────

    [Fact]
    public void Implements_IPairingService()
    {
        var (svc, _) = CreateServiceWithClock();
        Assert.IsAssignableFrom<IPairingService>(svc);
    }

    // ── Initial state ──────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_CurrentPin_IsNull()
    {
        var (svc, _) = CreateServiceWithClock();
        Assert.Null(svc.CurrentPin);
    }

    [Fact]
    public void InitialState_IsPinExpired_IsTrue()
    {
        var (svc, _) = CreateServiceWithClock();
        Assert.True(svc.IsPinExpired);
    }

    [Fact]
    public void InitialState_IsLockedOut_IsFalse()
    {
        var (svc, _) = CreateServiceWithClock();
        Assert.False(svc.IsLockedOut);
    }

    [Fact]
    public void InitialState_AttemptsRemaining_EqualsMaxAttempts()
    {
        var (svc, _) = CreateServiceWithClock(maxAttempts: 5);
        Assert.Equal(5, svc.AttemptsRemaining);
    }

    // ── GeneratePin ────────────────────────────────────────────────────────────

    [Fact]
    public void GeneratePin_Returns6DigitNumericString()
    {
        var (svc, _) = CreateServiceWithClock();
        var pin = svc.GeneratePin();

        Assert.NotNull(pin);
        Assert.Equal(6, pin.Length);
        Assert.True(long.TryParse(pin, out _), "PIN should be numeric");
    }

    [Fact]
    public void GeneratePin_SetsCurrentPin()
    {
        var (svc, _) = CreateServiceWithClock();
        var pin = svc.GeneratePin();
        Assert.Equal(pin, svc.CurrentPin);
    }

    [Fact]
    public void GeneratePin_Sets_IsPinExpired_False()
    {
        var (svc, _) = CreateServiceWithClock();
        svc.GeneratePin();
        Assert.False(svc.IsPinExpired);
    }

    [Fact]
    public void GeneratePin_ResetsFailedAttempts()
    {
        var (svc, _) = CreateServiceWithClock();
        svc.GeneratePin();
        // Burn some attempts
        svc.ValidatePin("000000");
        svc.ValidatePin("000000");
        Assert.Equal(3, svc.AttemptsRemaining);

        // Regenerating should reset
        svc.GeneratePin();
        Assert.Equal(5, svc.AttemptsRemaining);
    }

    [Fact]
    public void GeneratePin_Fires_PinGenerated_Event()
    {
        var (svc, _) = CreateServiceWithClock();
        string? receivedPin = null;
        svc.PinGenerated += (_, pin) => receivedPin = pin;

        var returned = svc.GeneratePin();

        Assert.NotNull(receivedPin);
        Assert.Equal(returned, receivedPin);
    }

    [Fact]
    public void GeneratePin_CalledTwice_ProducesNewPin()
    {
        // Very occasionally two consecutive calls could produce the same PIN by chance
        // (1 in 900000 probability). We run this 3 times and require at least one change.
        var (svc, _) = CreateServiceWithClock();
        var pins = new HashSet<string>();
        for (int i = 0; i < 3; i++) pins.Add(svc.GeneratePin());
        // With 3 attempts the probability of all three being identical is negligible
        Assert.True(pins.Count >= 2, "GeneratePin should produce different values on repeated calls");
    }

    // ── RefreshPin ─────────────────────────────────────────────────────────────

    [Fact]
    public void RefreshPin_GeneratesNewPin_AndResetsState()
    {
        var (svc, _) = CreateServiceWithClock();
        svc.GeneratePin();
        svc.ValidatePin("000000"); // one failed attempt
        svc.RefreshPin();

        Assert.NotNull(svc.CurrentPin);
        Assert.Equal(5, svc.AttemptsRemaining); // reset
        Assert.False(svc.IsPinExpired);
    }

    // ── ValidatePin — success ──────────────────────────────────────────────────

    [Fact]
    public void ValidatePin_CorrectPin_ReturnsTrue()
    {
        var (svc, _) = CreateServiceWithClock();
        var pin = svc.GeneratePin();
        Assert.True(svc.ValidatePin(pin));
    }

    [Fact]
    public void ValidatePin_CorrectPin_FiresPairingAttempted_WithSuccess()
    {
        var (svc, _) = CreateServiceWithClock();
        var pin = svc.GeneratePin();

        PairingAttemptResult? result = null;
        svc.PairingAttempted += (_, r) => result = r;

        svc.ValidatePin(pin);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Null(result.FailureReason);
    }

    // ── ValidatePin — wrong PIN ────────────────────────────────────────────────

    [Fact]
    public void ValidatePin_IncorrectPin_ReturnsFalse()
    {
        var (svc, _) = CreateServiceWithClock();
        svc.GeneratePin();
        Assert.False(svc.ValidatePin("000000"));
    }

    [Fact]
    public void ValidatePin_IncorrectPin_Fires_PairingAttempted_WithInvalidPin()
    {
        var (svc, _) = CreateServiceWithClock();
        svc.GeneratePin();

        PairingAttemptResult? result = null;
        svc.PairingAttempted += (_, r) => result = r;

        svc.ValidatePin("000000");

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(PairingFailureReason.InvalidPin, result.FailureReason);
    }

    [Fact]
    public void ValidatePin_IncorrectPin_DecrementsAttemptsRemaining()
    {
        var (svc, _) = CreateServiceWithClock();
        svc.GeneratePin();

        svc.ValidatePin("000000");

        Assert.Equal(4, svc.AttemptsRemaining);
    }

    [Fact]
    public void ValidatePin_NullPin_ReturnsFalse()
    {
        var (svc, _) = CreateServiceWithClock();
        svc.GeneratePin();
        Assert.False(svc.ValidatePin(null!));
    }

    [Fact]
    public void ValidatePin_EmptyPin_ReturnsFalse()
    {
        var (svc, _) = CreateServiceWithClock();
        svc.GeneratePin();
        Assert.False(svc.ValidatePin(string.Empty));
    }

    // ── ValidatePin — no PIN generated yet ────────────────────────────────────

    [Fact]
    public void ValidatePin_BeforeGenerate_ReturnsFalse_WithExpiredReason()
    {
        var (svc, _) = CreateServiceWithClock();
        // Do NOT call GeneratePin

        PairingAttemptResult? result = null;
        svc.PairingAttempted += (_, r) => result = r;

        bool valid = svc.ValidatePin("123456");

        Assert.False(valid);
        Assert.NotNull(result);
        Assert.Equal(PairingFailureReason.PinExpired, result!.FailureReason);
    }

    // ── ValidatePin — expiry ───────────────────────────────────────────────────

    [Fact]
    public void ValidatePin_AfterPinExpires_ReturnsFalse_WithExpiredReason()
    {
        var (svc, clock) = CreateServiceWithClock(pinTtl: TimeSpan.FromMinutes(5));
        var pin = svc.GeneratePin();

        // Advance time past TTL
        clock.Advance(TimeSpan.FromMinutes(6));

        PairingAttemptResult? result = null;
        svc.PairingAttempted += (_, r) => result = r;

        bool valid = svc.ValidatePin(pin);

        Assert.False(valid);
        Assert.NotNull(result);
        Assert.Equal(PairingFailureReason.PinExpired, result!.FailureReason);
    }

    [Fact]
    public void IsPinExpired_TrueAfterTtlElapses()
    {
        var (svc, clock) = CreateServiceWithClock(pinTtl: TimeSpan.FromMinutes(5));
        svc.GeneratePin();

        clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));

        Assert.True(svc.IsPinExpired);
    }

    [Fact]
    public void IsPinExpired_FalseBeforeTtlElapses()
    {
        var (svc, clock) = CreateServiceWithClock(pinTtl: TimeSpan.FromMinutes(5));
        svc.GeneratePin();

        clock.Advance(TimeSpan.FromMinutes(4));

        Assert.False(svc.IsPinExpired);
    }

    // ── ValidatePin — lockout ──────────────────────────────────────────────────

    [Fact]
    public void ValidatePin_LocksOut_AfterMaxFailedAttempts()
    {
        var (svc, _) = CreateServiceWithClock(maxAttempts: 3);
        svc.GeneratePin();

        svc.ValidatePin("000000");
        svc.ValidatePin("000000");
        svc.ValidatePin("000000");

        Assert.True(svc.IsLockedOut);
        Assert.Equal(0, svc.AttemptsRemaining);
    }

    [Fact]
    public void ValidatePin_WhenLockedOut_ReturnsFalse_WithTooManyAttemptsReason()
    {
        var (svc, _) = CreateServiceWithClock(maxAttempts: 2);
        var pin = svc.GeneratePin();

        svc.ValidatePin("000000");
        svc.ValidatePin("000000"); // now locked out

        PairingAttemptResult? result = null;
        svc.PairingAttempted += (_, r) => result = r;

        // Even a correct pin should fail when locked out
        bool valid = svc.ValidatePin(pin);

        Assert.False(valid);
        Assert.NotNull(result);
        Assert.Equal(PairingFailureReason.TooManyAttempts, result!.FailureReason);
    }

    [Fact]
    public void RefreshPin_UnlocksAfterLockout()
    {
        var (svc, _) = CreateServiceWithClock(maxAttempts: 2);
        svc.GeneratePin();
        svc.ValidatePin("000000");
        svc.ValidatePin("000000");
        Assert.True(svc.IsLockedOut);

        svc.RefreshPin();
        var newPin = svc.CurrentPin!;

        Assert.False(svc.IsLockedOut);
        Assert.True(svc.ValidatePin(newPin));
    }

    // ── MaxAttempts property ───────────────────────────────────────────────────

    [Fact]
    public void MaxAttempts_ReflectsConstructorArgument()
    {
        var (svc, _) = CreateServiceWithClock(maxAttempts: 7);
        Assert.Equal(7, svc.MaxAttempts);
    }

    // ── PairingAttempted event always fires ────────────────────────────────────

    [Fact]
    public void PairingAttempted_Fires_OnEveryValidateCall()
    {
        var (svc, _) = CreateServiceWithClock();
        var pin = svc.GeneratePin();

        int count = 0;
        svc.PairingAttempted += (_, _) => count++;

        svc.ValidatePin("000000"); // fail
        svc.ValidatePin(pin);      // success

        Assert.Equal(2, count);
    }
}
