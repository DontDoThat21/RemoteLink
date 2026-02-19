using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class WakeOnLanServiceTests
{
    private readonly ILogger<WakeOnLanService> _logger;
    private readonly WakeOnLanService _service;

    public WakeOnLanServiceTests()
    {
        _logger = NullLogger<WakeOnLanService>.Instance;
        _service = new WakeOnLanService(_logger);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new WakeOnLanService(null!));
    }

    [Theory]
    [InlineData("00:11:22:33:44:55", true)]
    [InlineData("AA:BB:CC:DD:EE:FF", true)]
    [InlineData("aa:bb:cc:dd:ee:ff", true)]
    [InlineData("00-11-22-33-44-55", true)]
    [InlineData("AA-BB-CC-DD-EE-FF", true)]
    [InlineData("aa-bb-cc-dd-ee-ff", true)]
    [InlineData("FF:FF:FF:FF:FF:FF", true)]
    [InlineData("00:00:00:00:00:00", true)]
    public void IsValidMacAddress_ValidFormats_ReturnsTrue(string macAddress, bool expected)
    {
        bool result = _service.IsValidMacAddress(macAddress);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(null, false)]
    [InlineData("00:11:22:33:44", false)] // Too short
    [InlineData("00:11:22:33:44:55:66", false)] // Too long
    [InlineData("GG:11:22:33:44:55", false)] // Invalid hex
    [InlineData("00-11-22:33:44:55", false)] // Mixed separators
    [InlineData("001122334455", false)] // No separators
    [InlineData("00.11.22.33.44.55", false)] // Wrong separator
    [InlineData("0:1:2:3:4:5", false)] // Single digit bytes
    public void IsValidMacAddress_InvalidFormats_ReturnsFalse(string? macAddress, bool expected)
    {
        bool result = _service.IsValidMacAddress(macAddress!);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task SendWakePacketAsync_NullMacAddress_ReturnsFalse()
    {
        bool result = await _service.SendWakePacketAsync(null!);
        Assert.False(result);
    }

    [Fact]
    public async Task SendWakePacketAsync_EmptyMacAddress_ReturnsFalse()
    {
        bool result = await _service.SendWakePacketAsync("");
        Assert.False(result);
    }

    [Fact]
    public async Task SendWakePacketAsync_WhitespaceMacAddress_ReturnsFalse()
    {
        bool result = await _service.SendWakePacketAsync("   ");
        Assert.False(result);
    }

    [Fact]
    public async Task SendWakePacketAsync_InvalidMacAddress_ReturnsFalse()
    {
        bool result = await _service.SendWakePacketAsync("invalid-mac");
        Assert.False(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(100000)]
    public async Task SendWakePacketAsync_InvalidPort_ReturnsFalse(int port)
    {
        bool result = await _service.SendWakePacketAsync("00:11:22:33:44:55", port: port);
        Assert.False(result);
    }

    [Theory]
    [InlineData("00:11:22:33:44:55")]
    [InlineData("AA-BB-CC-DD-EE-FF")]
    [InlineData("ff:ff:ff:ff:ff:ff")]
    public async Task SendWakePacketAsync_ValidMacAddress_ReturnsTrue(string macAddress)
    {
        // Note: This sends an actual UDP packet to the broadcast address
        // It won't wake any device in tests, but validates the packet is sent
        bool result = await _service.SendWakePacketAsync(macAddress);
        Assert.True(result);
    }

    [Fact]
    public async Task SendWakePacketAsync_CustomBroadcastAddress_ReturnsTrue()
    {
        bool result = await _service.SendWakePacketAsync("00:11:22:33:44:55", "192.168.1.255");
        Assert.True(result);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(12287)] // Alternative WOL port
    public async Task SendWakePacketAsync_CustomPort_ReturnsTrue(int port)
    {
        bool result = await _service.SendWakePacketAsync("00:11:22:33:44:55", port: port);
        Assert.True(result);
    }

    [Fact]
    public async Task WakeDeviceAsync_NullDevice_ReturnsFalse()
    {
        bool result = await _service.WakeDeviceAsync(null!);
        Assert.False(result);
    }

    [Fact]
    public async Task WakeDeviceAsync_DeviceWithoutMacAddress_ReturnsFalse()
    {
        var device = new DeviceInfo
        {
            DeviceId = "test-device",
            DeviceName = "Test Device",
            IPAddress = "192.168.1.100",
            MacAddress = null
        };

        bool result = await _service.WakeDeviceAsync(device);
        Assert.False(result);
    }

    [Fact]
    public async Task WakeDeviceAsync_DeviceWithEmptyMacAddress_ReturnsFalse()
    {
        var device = new DeviceInfo
        {
            DeviceId = "test-device",
            DeviceName = "Test Device",
            IPAddress = "192.168.1.100",
            MacAddress = ""
        };

        bool result = await _service.WakeDeviceAsync(device);
        Assert.False(result);
    }

    [Fact]
    public async Task WakeDeviceAsync_DeviceWithInvalidMacAddress_ReturnsFalse()
    {
        var device = new DeviceInfo
        {
            DeviceId = "test-device",
            DeviceName = "Test Device",
            IPAddress = "192.168.1.100",
            MacAddress = "invalid"
        };

        bool result = await _service.WakeDeviceAsync(device);
        Assert.False(result);
    }

    [Fact]
    public async Task WakeDeviceAsync_ValidDevice_ReturnsTrue()
    {
        var device = new DeviceInfo
        {
            DeviceId = "test-device",
            DeviceName = "Test Device",
            IPAddress = "192.168.1.100",
            MacAddress = "00:11:22:33:44:55"
        };

        bool result = await _service.WakeDeviceAsync(device);
        Assert.True(result);
    }

    [Fact]
    public async Task WakeDeviceAsync_CustomBroadcast_ReturnsTrue()
    {
        var device = new DeviceInfo
        {
            DeviceId = "test-device",
            DeviceName = "Test Device",
            IPAddress = "192.168.1.100",
            MacAddress = "AA:BB:CC:DD:EE:FF"
        };

        bool result = await _service.WakeDeviceAsync(device, "192.168.1.255");
        Assert.True(result);
    }

    [Fact]
    public async Task WakeDeviceAsync_CustomPort_ReturnsTrue()
    {
        var device = new DeviceInfo
        {
            DeviceId = "test-device",
            DeviceName = "Test Device",
            IPAddress = "192.168.1.100",
            MacAddress = "FF:FF:FF:FF:FF:FF"
        };

        bool result = await _service.WakeDeviceAsync(device, port: 7);
        Assert.True(result);
    }

    [Fact]
    public async Task SendWakePacketAsync_ColonSeparator_Succeeds()
    {
        bool result = await _service.SendWakePacketAsync("DE:AD:BE:EF:CA:FE");
        Assert.True(result);
    }

    [Fact]
    public async Task SendWakePacketAsync_HyphenSeparator_Succeeds()
    {
        bool result = await _service.SendWakePacketAsync("DE-AD-BE-EF-CA-FE");
        Assert.True(result);
    }

    [Fact]
    public async Task SendWakePacketAsync_MixedCase_Succeeds()
    {
        bool result = await _service.SendWakePacketAsync("aA:bB:cC:dD:eE:fF");
        Assert.True(result);
    }
}
