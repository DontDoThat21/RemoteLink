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
