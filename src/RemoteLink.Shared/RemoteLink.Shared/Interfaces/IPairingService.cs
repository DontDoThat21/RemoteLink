using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Manages PIN-based device pairing for the RemoteLink host.
/// <para>
/// Workflow:
/// <list type="number">
///   <item><description>Host calls <see cref="GeneratePin"/> and displays the result to the local user.</description></item>
///   <item><description>Remote client user enters the PIN in their app and sends a <see cref="PairingRequest"/>.</description></item>
///   <item><description>Host calls <see cref="ValidatePin(string)"/>; on success, the session is established.</description></item>
///   <item><description>After <see cref="MaxAttempts"/> failures the PIN is locked — call <see cref="RefreshPin"/> to reset.</description></item>
/// </list>
/// </para>
/// </summary>
public interface IPairingService
{
    // ── PIN management ────────────────────────────────────────────────────────

    /// <summary>
    /// Generate (or regenerate) a fresh PIN, reset the failure counter, and
    /// reset the expiry clock.  Fires <see cref="PinGenerated"/>.
    /// </summary>
    /// <returns>The newly generated PIN string.</returns>
    string GeneratePin();

    /// <summary>Alias for <see cref="GeneratePin"/>; regenerates the current PIN.</summary>
    void RefreshPin();

    /// <summary>
    /// Validate a PIN received from a remote client.
    /// Increments the internal failure counter on a wrong PIN.
    /// Fires <see cref="PairingAttempted"/> with the outcome.
    /// </summary>
    /// <param name="pin">PIN string supplied by the client.</param>
    /// <returns><c>true</c> if the PIN is correct, current, and the service is not locked out.</returns>
    bool ValidatePin(string pin);

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>The current PIN, or <c>null</c> if none has been generated yet.</summary>
    string? CurrentPin { get; }

    /// <summary>
    /// <c>true</c> when no PIN has been generated yet or the PIN has exceeded its TTL.
    /// </summary>
    bool IsPinExpired { get; }

    /// <summary>
    /// <c>true</c> when the maximum number of failed attempts has been reached.
    /// Call <see cref="RefreshPin"/> to reset.
    /// </summary>
    bool IsLockedOut { get; }

    /// <summary>Number of remaining attempts before lockout.</summary>
    int AttemptsRemaining { get; }

    /// <summary>Maximum failed attempts allowed before lockout.</summary>
    int MaxAttempts { get; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired whenever a new PIN is generated.  Event arg is the new PIN string.</summary>
    event EventHandler<string> PinGenerated;

    /// <summary>Fired after every <see cref="ValidatePin"/> call with the outcome.</summary>
    event EventHandler<PairingAttemptResult> PairingAttempted;
}
