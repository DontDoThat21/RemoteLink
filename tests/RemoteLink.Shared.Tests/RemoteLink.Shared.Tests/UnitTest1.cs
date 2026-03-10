using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Shared.Tests;

public class DeviceInfoTests
{
    [Fact]
    public void DeviceInfo_ShouldCreateWithDefaults()
    {
        // Arrange & Act
        var device = new DeviceInfo();

        // Assert
        Assert.Equal(string.Empty, device.DeviceId);
        Assert.Equal(string.Empty, device.DeviceName);
        Assert.Equal(string.Empty, device.IPAddress);
        Assert.Equal(0, device.Port);
        Assert.Equal(DeviceType.Unknown, device.Type);
        Assert.False(device.IsOnline);
    }

    [Fact]
    public void DeviceInfo_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var deviceId = "test-device-123";
        var deviceName = "Test Device";
        var ipAddress = "192.168.1.100";
        var port = 12345;
        var deviceType = DeviceType.Desktop;

        // Act
        var device = new DeviceInfo
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            IPAddress = ipAddress,
            Port = port,
            Type = deviceType,
            IsOnline = true
        };

        // Assert
        Assert.Equal(deviceId, device.DeviceId);
        Assert.Equal(deviceName, device.DeviceName);
        Assert.Equal(ipAddress, device.IPAddress);
        Assert.Equal(port, device.Port);
        Assert.Equal(deviceType, device.Type);
        Assert.True(device.IsOnline);
    }

    [Fact]
    public void DeviceIdentityManager_FormatInternetDeviceId_ShouldInsertSpaces()
    {
        Assert.Equal("123 456 789", DeviceIdentityManager.FormatInternetDeviceId("123456789"));
    }

    [Fact]
    public void DeviceIdentityManager_MatchesDevice_ShouldMatchSavedDeviceAgainstInternetIdOnlyTarget()
    {
        var saved = new SavedDevice
        {
            DeviceId = "desktop-local-id",
            InternetDeviceId = "123456789"
        };

        var target = new DeviceInfo
        {
            DeviceId = "123456789",
            DeviceName = "Relay Target"
        };

        Assert.True(DeviceIdentityManager.MatchesDevice(saved, target));
    }

    [Fact]
    public void DeviceIdentityManager_GetPreferredDisplayId_ShouldPreferInternetDeviceId()
    {
        var device = new DeviceInfo
        {
            DeviceId = "desktop-local-id",
            InternetDeviceId = "987654321",
            DeviceName = "Desktop"
        };

        Assert.Equal("987 654 321", DeviceIdentityManager.GetPreferredDisplayId(device));
    }
}