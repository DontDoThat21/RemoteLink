using System.Security.Cryptography;
using System.Text;
using RemoteLink.Shared.Interfaces;

namespace RemoteLink.Mobile.Services;

public sealed class AppLockService : IAppLockService
{
    private const string PinStorageKey = "remotelink.mobile.app_lock.pin_hash";

    private readonly IAppSettingsService _settingsService;
    private DateTime? _backgroundedAtUtc;
    private DateTime? _lastUnlockedAtUtc;

    public AppLockService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool IsLockEnabled => _settingsService.Current.Security.EnableAppLock;

    public bool IsLocked { get; private set; }

    public event EventHandler<bool>? LockStateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!IsLockEnabled || !await HasPinAsync(cancellationToken))
        {
            SetLocked(false);
            return;
        }

        if (_lastUnlockedAtUtc is null)
            SetLocked(true);
    }

    public async Task<bool> HasPinAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stored = await GetStoredPinHashAsync();
        return !string.IsNullOrWhiteSpace(stored);
    }

    public async Task SetPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(pin) || pin.Length != 6 || !pin.All(char.IsDigit))
            throw new ArgumentException("App lock PIN must be exactly 6 digits.", nameof(pin));

        var hash = ComputeHash(pin);
        await SetStoredPinHashAsync(hash);
        SetLocked(true);
    }

    public Task ClearPinAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            SecureStorage.Default.Remove(PinStorageKey);
        }
        catch
        {
        }

        Preferences.Default.Remove(PinStorageKey);
        _backgroundedAtUtc = null;
        _lastUnlockedAtUtc = null;
        SetLocked(false);
        return Task.CompletedTask;
    }

    public async Task<bool> VerifyPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(pin))
            return false;

        var stored = await GetStoredPinHashAsync();
        var success = !string.IsNullOrWhiteSpace(stored)
            && string.Equals(stored, ComputeHash(pin), StringComparison.Ordinal);

        if (success)
            MarkUnlocked();
        else
            SetLocked(true);

        return success;
    }

    public async Task<bool> ShouldLockAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsLockEnabled || !await HasPinAsync(cancellationToken))
        {
            SetLocked(false);
            return false;
        }

        if (IsLocked || _lastUnlockedAtUtc is null)
        {
            SetLocked(true);
            return true;
        }

        if (_backgroundedAtUtc is null)
            return false;

        var timeoutMinutes = Math.Max(0, _settingsService.Current.Security.AppLockTimeoutMinutes);
        var shouldLock = timeoutMinutes == 0
            || DateTime.UtcNow - _backgroundedAtUtc.Value >= TimeSpan.FromMinutes(timeoutMinutes);

        if (shouldLock)
            SetLocked(true);

        return shouldLock;
    }

    public void MarkBackgrounded()
    {
        if (IsLockEnabled)
            _backgroundedAtUtc = DateTime.UtcNow;
    }

    public void MarkUnlocked()
    {
        _backgroundedAtUtc = null;
        _lastUnlockedAtUtc = DateTime.UtcNow;
        SetLocked(false);
    }

    public void ForceLock()
    {
        if (IsLockEnabled)
            SetLocked(true);
    }

    private void SetLocked(bool locked)
    {
        if (IsLocked == locked)
            return;

        IsLocked = locked;
        LockStateChanged?.Invoke(this, locked);
    }

    private static string ComputeHash(string pin)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
        return Convert.ToHexString(hash);
    }

    private static async Task<string?> GetStoredPinHashAsync()
    {
        try
        {
            var secureValue = await SecureStorage.Default.GetAsync(PinStorageKey);
            if (!string.IsNullOrWhiteSpace(secureValue))
                return secureValue;
        }
        catch
        {
        }

        return Preferences.Default.Get(PinStorageKey, string.Empty);
    }

    private static async Task SetStoredPinHashAsync(string hash)
    {
        try
        {
            await SecureStorage.Default.SetAsync(PinStorageKey, hash);
            Preferences.Default.Remove(PinStorageKey);
            return;
        }
        catch
        {
        }

        Preferences.Default.Set(PinStorageKey, hash);
    }
}