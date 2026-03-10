using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Security;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public sealed class UserAccountServiceTests : IDisposable
{
    private readonly string _storageDirectory = Path.Combine(Path.GetTempPath(), "RemoteLinkTests", "Accounts", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RegisterAsync_CreatesPersistentSession()
    {
        var service = CreateService();

        var session = await service.RegisterAsync("Alice@example.com", "Sup3rSecret!", "Alice Admin");

        Assert.True(service.IsSignedIn);
        Assert.NotNull(service.CurrentSession);
        Assert.Equal("alice@example.com", session.Email);

        var reloaded = CreateService();
        await reloaded.LoadAsync();

        Assert.True(reloaded.IsSignedIn);
        Assert.Equal(session.AccountId, reloaded.CurrentSession!.AccountId);
        Assert.Equal(session.Email, reloaded.CurrentSession.Email);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_Throws()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterAsync("ALICE@example.com", "An0therSecret!", "Alice Two"));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_Throws()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");
        await service.LogoutAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync("alice@example.com", "wrong-pass"));
    }

    [Fact]
    public async Task RegisterDeviceAsync_TracksAndRemovesManagedDevices()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "desktop-01",
            InternetDeviceId = "123 456 789",
            DeviceName = "Office PC",
            IPAddress = "192.168.1.20",
            Port = 12346,
            Type = DeviceType.Desktop,
            SupportsRelay = true,
            RelayServerHost = "relay.example.test",
            RelayServerPort = 12400
        });

        var devices = await service.GetManagedDevicesAsync();
        Assert.Single(devices);
        Assert.Equal("123456789", devices[0].InternetDeviceId);

        await service.RemoveManagedDeviceAsync("123 456 789");
        devices = await service.GetManagedDevicesAsync();

        Assert.Empty(devices);
    }

    [Fact]
    public async Task SyncSavedDevicesAsync_ReplacesSnapshotAndDeduplicatesByIdentity()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.SyncSavedDevicesAsync(new[]
        {
            new SavedDevice
            {
                Id = "one",
                FriendlyName = "Office PC",
                DeviceName = "DESKTOP-01",
                DeviceId = "desktop-01",
                InternetDeviceId = "123456789",
                IPAddress = "192.168.1.20",
                Port = 12346,
                LastConnected = DateTime.UtcNow.AddMinutes(-5)
            },
            new SavedDevice
            {
                Id = "two",
                FriendlyName = "Office PC Updated",
                DeviceName = "DESKTOP-01",
                DeviceId = "desktop-01",
                InternetDeviceId = "123 456 789",
                IPAddress = "10.0.0.20",
                Port = 12346,
                LastConnected = DateTime.UtcNow
            }
        });

        var synced = await service.GetSyncedSavedDevicesAsync();

        Assert.Single(synced);
        Assert.Equal("Office PC Updated", synced[0].FriendlyName);
        Assert.Equal("123456789", synced[0].InternetDeviceId);
        Assert.Equal("10.0.0.20", synced[0].IPAddress);
    }

    [Fact]
    public async Task GetCurrentProfileAsync_ReturnsAccountData()
    {
        var service = CreateService();
        var session = await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "mobile-01",
            DeviceName = "Alice Phone",
            IPAddress = "192.168.1.30",
            Port = 12347,
            Type = DeviceType.Mobile
        });

        var profile = await service.GetCurrentProfileAsync();

        Assert.NotNull(profile);
        Assert.Equal(session.AccountId, profile!.AccountId);
        Assert.Equal("Alice", profile.DisplayName);
        Assert.Single(profile.ManagedDevices);
    }

    [Fact]
    public async Task UpsertConnectionAuditLogEntryAsync_PersistsAndReturnsNewestFirst()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.UpsertConnectionAuditLogEntryAsync(new ConnectionAuditLogEntry
        {
            AuditId = "older",
            ClientDeviceId = "device-1",
            ClientDeviceName = "Office PC",
            RequestedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ConnectedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            DisconnectedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            Duration = TimeSpan.FromMinutes(4),
            Outcome = ConnectionAuditOutcome.Disconnected,
            Actions = new List<ConnectionAuditActionEntry>
            {
                new() { ActionType = ConnectionAuditActionType.PairingAccepted, Description = "Connected" }
            }
        });

        await service.UpsertConnectionAuditLogEntryAsync(new ConnectionAuditLogEntry
        {
            AuditId = "newer",
            ClientDeviceId = "device-2",
            ClientDeviceName = "Alice Phone",
            RequestedAtUtc = DateTime.UtcNow,
            Outcome = ConnectionAuditOutcome.RejectedInvalidPin,
            Actions = new List<ConnectionAuditActionEntry>
            {
                new() { ActionType = ConnectionAuditActionType.PairingRejected, Description = "Invalid PIN" }
            }
        });

        var reloaded = CreateService();
        await reloaded.LoadAsync();

        var entries = await reloaded.GetConnectionAuditLogAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("newer", entries[0].AuditId);
        Assert.Equal("older", entries[1].AuditId);
        Assert.Equal(ConnectionAuditActionType.PairingRejected, Assert.Single(entries[0].Actions).ActionType);
    }

    [Fact]
    public async Task UpsertConnectionAuditLogEntryAsync_ReplacesExistingEntryBySessionId()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.UpsertConnectionAuditLogEntryAsync(new ConnectionAuditLogEntry
        {
            AuditId = "audit-1",
            SessionId = "session-1",
            ClientDeviceId = "device-1",
            ClientDeviceName = "Alice Phone",
            RequestedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            ConnectedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            Outcome = ConnectionAuditOutcome.Connected,
            Actions = new List<ConnectionAuditActionEntry>
            {
                new() { ActionType = ConnectionAuditActionType.PairingAccepted, Description = "Connected" }
            }
        });

        await service.UpsertConnectionAuditLogEntryAsync(new ConnectionAuditLogEntry
        {
            AuditId = "audit-2",
            SessionId = "session-1",
            ClientDeviceId = "device-1",
            ClientDeviceName = "Alice Phone",
            RequestedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            ConnectedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            DisconnectedAtUtc = DateTime.UtcNow,
            Duration = TimeSpan.FromMinutes(1),
            Outcome = ConnectionAuditOutcome.Disconnected,
            Actions = new List<ConnectionAuditActionEntry>
            {
                new() { ActionType = ConnectionAuditActionType.PairingAccepted, Description = "Connected" },
                new() { ActionType = ConnectionAuditActionType.SessionDisconnected, Description = "Disconnected" }
            }
        });

        var entries = await service.GetConnectionAuditLogAsync();
        var entry = Assert.Single(entries);
        Assert.Equal("audit-2", entry.AuditId);
        Assert.Equal(ConnectionAuditOutcome.Disconnected, entry.Outcome);
        Assert.Equal(2, entry.Actions.Count);

        var profile = await service.GetCurrentProfileAsync();
        Assert.NotNull(profile);
        Assert.Single(profile!.ConnectionAuditLog);
    }

    [Fact]
    public async Task SetDeviceTrustAsync_PersistsAndMatchesByInternetDeviceId()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "mobile-01",
            InternetDeviceId = "123 456 789",
            DeviceName = "Alice Phone",
            IPAddress = "192.168.1.30",
            Port = 12347,
            Type = DeviceType.Mobile
        });

        await service.SetDeviceTrustAsync("123 456 789", isTrusted: true);

        Assert.True(await service.IsDeviceTrustedAsync("mobile-01"));

        var reloaded = CreateService();
        await reloaded.LoadAsync();

        Assert.True(await reloaded.IsDeviceTrustedAsync("other-local-id", "123456789"));

        var profile = await reloaded.GetCurrentProfileAsync();
        Assert.NotNull(profile);
        Assert.True(profile!.ManagedDevices[0].IsTrusted);
        Assert.NotNull(profile.ManagedDevices[0].TrustedAtUtc);
    }

    [Fact]
    public async Task RegisterDeviceAsync_PreservesTrustFlagAcrossDeviceUpdates()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "mobile-01",
            InternetDeviceId = "123456789",
            DeviceName = "Alice Phone",
            IPAddress = "192.168.1.30",
            Port = 12347,
            Type = DeviceType.Mobile
        });
        await service.SetDeviceTrustAsync("mobile-01", isTrusted: true);

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "mobile-01",
            InternetDeviceId = "123 456 789",
            DeviceName = "Alice Phone Updated",
            IPAddress = "10.0.0.30",
            Port = 22347,
            Type = DeviceType.Mobile
        });

        var devices = await service.GetManagedDevicesAsync();
        var device = Assert.Single(devices);
        Assert.True(device.IsTrusted);
        Assert.NotNull(device.TrustedAtUtc);
        Assert.Equal("Alice Phone Updated", device.DeviceName);
        Assert.Equal("10.0.0.30", device.IPAddress);
        Assert.Equal(22347, device.Port);
    }

    [Fact]
    public async Task SetDeviceBlockedAsync_PersistsAndMatchesByInternetDeviceId()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "mobile-01",
            InternetDeviceId = "123 456 789",
            DeviceName = "Alice Phone",
            IPAddress = "192.168.1.30",
            Port = 12347,
            Type = DeviceType.Mobile
        });

        await service.SetDeviceBlockedAsync("123 456 789", isBlocked: true);

        Assert.True(await service.IsDeviceBlockedAsync("mobile-01"));
        Assert.False(await service.IsDeviceTrustedAsync("mobile-01"));

        var reloaded = CreateService();
        await reloaded.LoadAsync();

        Assert.True(await reloaded.IsDeviceBlockedAsync("other-local-id", "123456789"));

        var profile = await reloaded.GetCurrentProfileAsync();
        Assert.NotNull(profile);
        Assert.True(profile!.ManagedDevices[0].IsBlocked);
        Assert.NotNull(profile.ManagedDevices[0].BlockedAtUtc);
    }

    [Fact]
    public async Task SetDeviceBlockedAsync_CanCreatePlaceholderForUnknownIdentifier()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.SetDeviceBlockedAsync("987 654 321", isBlocked: true);

        Assert.True(await service.IsDeviceBlockedAsync("unknown-device", "987654321"));

        var devices = await service.GetManagedDevicesAsync();
        var device = Assert.Single(devices);
        Assert.Equal("987654321", device.InternetDeviceId);
        Assert.True(device.IsBlocked);
        Assert.Equal(DeviceType.Unknown, device.Type);
    }

    [Fact]
    public async Task SetDeviceSessionPermissionsAsync_PersistsAndMatchesByInternetDeviceId()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "mobile-01",
            InternetDeviceId = "123 456 789",
            DeviceName = "Alice Phone",
            IPAddress = "192.168.1.30",
            Port = 12347,
            Type = DeviceType.Mobile
        });

        await service.SetDeviceSessionPermissionsAsync("123 456 789", new SessionPermissionSet
        {
            AllowRemoteInput = false,
            AllowClipboardSync = false,
            AllowFileTransfer = false,
            AllowAudioStreaming = true,
            AllowSessionControl = false
        });

        var permissions = await service.GetDeviceSessionPermissionsAsync("mobile-01");
        Assert.False(permissions.AllowRemoteInput);
        Assert.False(permissions.AllowClipboardSync);
        Assert.False(permissions.AllowFileTransfer);

        var reloaded = CreateService();
        await reloaded.LoadAsync();

        var persisted = await reloaded.GetDeviceSessionPermissionsAsync("other-local-id", "123456789");
        Assert.False(persisted.AllowRemoteInput);
        Assert.False(persisted.AllowSessionControl);
    }

    [Fact]
    public async Task RegisterDeviceAsync_PreservesSessionPermissionsAcrossDeviceUpdates()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "mobile-01",
            InternetDeviceId = "123456789",
            DeviceName = "Alice Phone",
            IPAddress = "192.168.1.30",
            Port = 12347,
            Type = DeviceType.Mobile
        });

        await service.SetDeviceSessionPermissionsAsync("mobile-01", new SessionPermissionSet
        {
            AllowRemoteInput = false,
            AllowClipboardSync = false
        });

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "mobile-01",
            InternetDeviceId = "123 456 789",
            DeviceName = "Alice Phone Updated",
            IPAddress = "10.0.0.30",
            Port = 22347,
            Type = DeviceType.Mobile
        });

        var permissions = await service.GetDeviceSessionPermissionsAsync("mobile-01");
        Assert.False(permissions.AllowRemoteInput);
        Assert.False(permissions.AllowClipboardSync);
    }

    [Fact]
    public async Task RegisterDeviceAsync_PreservesBlockFlagAcrossDeviceUpdates()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "mobile-01",
            InternetDeviceId = "123456789",
            DeviceName = "Alice Phone",
            IPAddress = "192.168.1.30",
            Port = 12347,
            Type = DeviceType.Mobile
        });
        await service.SetDeviceBlockedAsync("mobile-01", isBlocked: true);

        await service.RegisterDeviceAsync(new DeviceInfo
        {
            DeviceId = "mobile-01",
            InternetDeviceId = "123 456 789",
            DeviceName = "Alice Phone Updated",
            IPAddress = "10.0.0.30",
            Port = 22347,
            Type = DeviceType.Mobile
        });

        var devices = await service.GetManagedDevicesAsync();
        var device = Assert.Single(devices);
        Assert.True(device.IsBlocked);
        Assert.NotNull(device.BlockedAtUtc);
        Assert.Equal("Alice Phone Updated", device.DeviceName);
        Assert.Equal("10.0.0.30", device.IPAddress);
        Assert.Equal(22347, device.Port);
    }

    [Fact]
    public async Task BeginTwoFactorSetupAsync_ReturnsProvisioningUri()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");

        var setup = await service.BeginTwoFactorSetupAsync();

        Assert.False(string.IsNullOrWhiteSpace(setup.SharedSecret));
        Assert.Contains("otpauth://totp/", setup.ProvisioningUri, StringComparison.Ordinal);
        Assert.Contains("alice%40example.com", setup.ProvisioningUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnableTwoFactorAsync_RequiresValidCodeForLogin()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");
        var setup = await service.BeginTwoFactorSetupAsync();

        await service.EnableTwoFactorAsync(TotpAuthenticator.GenerateCode(setup.SharedSecret), requireForUnattendedAccess: true);
        await service.LogoutAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync("alice@example.com", "Sup3rSecret!"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync("alice@example.com", "Sup3rSecret!", "000000"));

        var session = await service.LoginAsync(
            "alice@example.com",
            "Sup3rSecret!",
            TotpAuthenticator.GenerateCode(setup.SharedSecret));

        Assert.True(session.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task ValidateTwoFactorCodeAsync_ReturnsTrueForCurrentCode()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");
        var setup = await service.BeginTwoFactorSetupAsync();
        var code = TotpAuthenticator.GenerateCode(setup.SharedSecret);

        await service.EnableTwoFactorAsync(code);

        Assert.True(await service.ValidateTwoFactorCodeAsync(TotpAuthenticator.GenerateCode(setup.SharedSecret)));
        Assert.False(await service.ValidateTwoFactorCodeAsync("123123"));
    }

    [Fact]
    public async Task IsTwoFactorRequiredForUnattendedAccessAsync_ReflectsEnrollmentChoice()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");
        var setup = await service.BeginTwoFactorSetupAsync();

        await service.EnableTwoFactorAsync(TotpAuthenticator.GenerateCode(setup.SharedSecret), requireForUnattendedAccess: true);

        Assert.True(await service.IsTwoFactorRequiredForUnattendedAccessAsync());
    }

    [Fact]
    public async Task DisableTwoFactorAsync_RestoresPasswordOnlyLogin()
    {
        var service = CreateService();
        await service.RegisterAsync("alice@example.com", "Sup3rSecret!", "Alice");
        var setup = await service.BeginTwoFactorSetupAsync();

        await service.EnableTwoFactorAsync(TotpAuthenticator.GenerateCode(setup.SharedSecret));
        await service.DisableTwoFactorAsync(TotpAuthenticator.GenerateCode(setup.SharedSecret));
        await service.LogoutAsync();

        var session = await service.LoginAsync("alice@example.com", "Sup3rSecret!");
        var profile = await service.GetCurrentProfileAsync();

        Assert.False(session.IsTwoFactorEnabled);
        Assert.NotNull(profile);
        Assert.False(profile!.IsTwoFactorEnabled);
    }

    private UserAccountService CreateService()
    {
        return new UserAccountService(NullLogger<UserAccountService>.Instance, _storageDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageDirectory))
            Directory.Delete(_storageDirectory, true);
    }
}
