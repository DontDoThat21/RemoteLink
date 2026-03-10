namespace RemoteLink.Shared.Models;

/// <summary>
/// Public profile details for a registered RemoteLink account.
/// </summary>
public sealed class UserAccountProfile
{
    public string AccountId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastLoginAtUtc { get; set; }
    public bool IsTwoFactorEnabled { get; set; }
    public bool RequireTwoFactorForUnattendedAccess { get; set; }
    public DateTime? TwoFactorEnabledAtUtc { get; set; }
    public List<AccountManagedDevice> ManagedDevices { get; set; } = new();
    public List<SavedDevice> SyncedSavedDevices { get; set; } = new();
}

/// <summary>
/// Represents an authenticated local account session.
/// </summary>
public sealed class UserAccountSession
{
    public string AccountId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsTwoFactorEnabled { get; set; }
}

/// <summary>
/// Authenticator-app enrollment details for TOTP-based two-factor authentication.
/// </summary>
public sealed class UserAccountTwoFactorSetup
{
    public string Issuer { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string SharedSecret { get; set; } = string.Empty;
    public string ProvisioningUri { get; set; } = string.Empty;
}

/// <summary>
/// Tracks a device associated with a user account for remote management.
/// </summary>
public sealed class AccountManagedDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string? InternetDeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public DeviceType Type { get; set; }
    public string IPAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool SupportsRelay { get; set; }
    public string? RelayServerHost { get; set; }
    public int? RelayServerPort { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}
