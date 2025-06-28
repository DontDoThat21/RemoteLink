using RemoteLink.Shared.Models;

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
}