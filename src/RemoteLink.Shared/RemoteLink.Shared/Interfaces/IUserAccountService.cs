using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Provides registration, login, session persistence, address-book sync, and device-management operations.
/// </summary>
public interface IUserAccountService
{
    /// <summary>Gets the current authenticated session, if any.</summary>
    UserAccountSession? CurrentSession { get; }

    /// <summary>Gets whether an account is currently authenticated.</summary>
    bool IsSignedIn { get; }

    /// <summary>Raised whenever the current session changes.</summary>
    event EventHandler<UserAccountSession?>? SessionChanged;

    /// <summary>Loads persisted account/session state from disk.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Registers a new account and signs it in immediately.</summary>
    Task<UserAccountSession> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Authenticates an existing account.</summary>
    Task<UserAccountSession> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Authenticates an existing account using password + TOTP when 2FA is enabled.</summary>
    Task<UserAccountSession> LoginAsync(string email, string password, string twoFactorCode, CancellationToken cancellationToken = default);

    /// <summary>Signs out the current session and clears the persisted session token.</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the full profile for the current account, if signed in.</summary>
    Task<UserAccountProfile?> GetCurrentProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>Registers or updates a device under the signed-in account.</summary>
    Task RegisterDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default);

    /// <summary>Removes a managed device by local or internet-facing identifier.</summary>
    Task RemoveManagedDeviceAsync(string deviceIdentifier, CancellationToken cancellationToken = default);

    /// <summary>Returns managed devices for the signed-in account.</summary>
    Task<IReadOnlyList<AccountManagedDevice>> GetManagedDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks or unmarks a managed device as trusted for PIN-less host pairing.</summary>
    Task SetDeviceTrustAsync(string deviceIdentifier, bool isTrusted, CancellationToken cancellationToken = default);

    /// <summary>Returns whether a managed device is trusted for PIN-less host pairing.</summary>
    Task<bool> IsDeviceTrustedAsync(string deviceIdentifier, string? internetDeviceId = null, CancellationToken cancellationToken = default);

    /// <summary>Replaces the synced saved-device snapshot for the signed-in account.</summary>
    Task SyncSavedDevicesAsync(IEnumerable<SavedDevice> savedDevices, CancellationToken cancellationToken = default);

    /// <summary>Returns the saved devices synced to the current account.</summary>
    Task<IReadOnlyList<SavedDevice>> GetSyncedSavedDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts TOTP enrollment for the current account and returns authenticator-app setup details.</summary>
    Task<UserAccountTwoFactorSetup> BeginTwoFactorSetupAsync(string? issuer = null, CancellationToken cancellationToken = default);

    /// <summary>Completes TOTP enrollment for the current account using a verification code.</summary>
    Task EnableTwoFactorAsync(string verificationCode, bool requireForUnattendedAccess = false, CancellationToken cancellationToken = default);

    /// <summary>Disables TOTP for the current account after verifying a current code.</summary>
    Task DisableTwoFactorAsync(string verificationCode, CancellationToken cancellationToken = default);

    /// <summary>Gets whether the signed-in account requires TOTP for unattended-access approval.</summary>
    Task<bool> IsTwoFactorRequiredForUnattendedAccessAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates a TOTP code for the signed-in account.</summary>
    Task<bool> ValidateTwoFactorCodeAsync(string code, CancellationToken cancellationToken = default);
}
