namespace RemoteLink.Mobile.Services;

public interface IAppLockService
{
    bool IsLockEnabled { get; }
    bool IsLocked { get; }

    event EventHandler<bool>? LockStateChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> HasPinAsync(CancellationToken cancellationToken = default);
    Task SetPinAsync(string pin, CancellationToken cancellationToken = default);
    Task ClearPinAsync(CancellationToken cancellationToken = default);
    Task<bool> VerifyPinAsync(string pin, CancellationToken cancellationToken = default);
    Task<bool> ShouldLockAsync(CancellationToken cancellationToken = default);
    void MarkBackgrounded();
    void MarkUnlocked();
    void ForceLock();
}