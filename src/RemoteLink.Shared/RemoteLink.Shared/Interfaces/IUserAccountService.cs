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

    /// <summary>Replaces the synced saved-device snapshot for the signed-in account.</summary>
    Task SyncSavedDevicesAsync(IEnumerable<SavedDevice> savedDevices, CancellationToken cancellationToken = default);

    /// <summary>Returns the saved devices synced to the current account.</summary>
    Task<IReadOnlyList<SavedDevice>> GetSyncedSavedDevicesAsync(CancellationToken cancellationToken = default);
}
