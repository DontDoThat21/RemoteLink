using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// PIN-based pairing service.
/// <para>
/// Generates a 6-digit numeric PIN, validates client-supplied PINs, enforces an
/// expiry window (default: 5 minutes) and a maximum-attempts lockout (default: 5).
/// </para>
/// <para>
/// An injectable <c>clock</c> delegate is accepted in the constructor so that
/// unit tests can fast-forward time without sleeping.
/// </para>
/// </summary>
public class PinPairingService : IPairingService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int DefaultMaxAttempts = 5;
    private const int PinDigits = 6;

    // Lower bound is 100000 so the result is always 6 digits (no leading zero loss).
    private const int PinMin = 100000;
    private const int PinMax = 999999; // inclusive

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly object _lock = new();
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _pinTtl;
    private readonly Random _rng;

    private string? _currentPin;
    private DateTime _pinGeneratedAt;
    private int _failedAttempts;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialise the service.
    /// </summary>
    /// <param name="clock">
    /// Optional UTC clock delegate.  Defaults to <see cref="DateTime.UtcNow"/>.
    /// Inject a controlled clock in unit tests.
    /// </param>
    /// <param name="pinTtl">
    /// How long a generated PIN remains valid.  Defaults to 5 minutes.
    /// </param>
    /// <param name="maxAttempts">
    /// Maximum failed attempts before lockout.  Defaults to 5.
    /// </param>
    public PinPairingService(
        Func<DateTime>? clock = null,
        TimeSpan? pinTtl = null,
        int maxAttempts = DefaultMaxAttempts)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        _pinTtl = pinTtl ?? TimeSpan.FromMinutes(5);
        MaxAttempts = maxAttempts;
        _rng = new Random();
        _pinGeneratedAt = DateTime.MinValue;
    }

    // ── IPairingService ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public int MaxAttempts { get; }

    /// <inheritdoc/>
    public string? CurrentPin
    {
        get { lock (_lock) return _currentPin; }
    }

    /// <inheritdoc/>
    public bool IsPinExpired
    {
        get
        {
            lock (_lock)
            {
                if (_currentPin == null) return true;
                return _clock() - _pinGeneratedAt > _pinTtl;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsLockedOut
    {
        get { lock (_lock) return _failedAttempts >= MaxAttempts; }
    }

    /// <inheritdoc/>
    public int AttemptsRemaining
    {
        get { lock (_lock) return Math.Max(0, MaxAttempts - _failedAttempts); }
    }

    /// <inheritdoc/>
    public event EventHandler<string>? PinGenerated;

    /// <inheritdoc/>
    public event EventHandler<PairingAttemptResult>? PairingAttempted;

    // ── Pin generation ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string GeneratePin()
    {
        string pin;

        lock (_lock)
        {
            pin = _rng.Next(PinMin, PinMax + 1).ToString();
            _currentPin = pin;
            _pinGeneratedAt = _clock();
            _failedAttempts = 0;
        }

        PinGenerated?.Invoke(this, pin);
        return pin;
    }

    /// <inheritdoc/>
    public void RefreshPin() => GeneratePin();

    // ── Validation ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool ValidatePin(string pin)
    {
        PairingAttemptResult result;

        lock (_lock)
        {
            // No PIN generated yet → treat as expired
            if (_currentPin == null)
            {
                result = new PairingAttemptResult
                {
                    Success = false,
                    FailureReason = PairingFailureReason.PinExpired
                };
                goto done;
            }

            // Lockout check
            if (_failedAttempts >= MaxAttempts)
            {
                result = new PairingAttemptResult
                {
                    Success = false,
                    FailureReason = PairingFailureReason.TooManyAttempts
                };
                goto done;
            }

            // Expiry check
            if (_clock() - _pinGeneratedAt > _pinTtl)
            {
                result = new PairingAttemptResult
                {
                    Success = false,
                    FailureReason = PairingFailureReason.PinExpired
                };
                goto done;
            }

            // PIN match
            if (string.IsNullOrEmpty(pin) || pin != _currentPin)
            {
                _failedAttempts++;
                result = new PairingAttemptResult
                {
                    Success = false,
                    FailureReason = PairingFailureReason.InvalidPin
                };
                goto done;
            }

            // Success — do NOT reset failed attempts here so audit history is preserved
            result = new PairingAttemptResult { Success = true };
        }

    done:
        PairingAttempted?.Invoke(this, result);
        return result.Success;
    }
}
