using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Service for sending Wake-on-LAN magic packets to wake remote devices.
/// </summary>
public interface IWakeOnLanService
{
    /// <summary>
    /// Sends a Wake-on-LAN magic packet to the specified MAC address.
    /// </summary>
    /// <param name="macAddress">MAC address in format "XX:XX:XX:XX:XX:XX" or "XX-XX-XX-XX-XX-XX"</param>
    /// <param name="broadcastAddress">Optional broadcast address (default: 255.255.255.255)</param>
    /// <param name="port">Optional WOL port (default: 9)</param>
    /// <returns>True if packet was sent successfully, false otherwise</returns>
    Task<bool> SendWakePacketAsync(string macAddress, string? broadcastAddress = null, int port = 9);

    /// <summary>
    /// Sends a Wake-on-LAN magic packet to wake the specified device.
    /// </summary>
    /// <param name="device">Device to wake (uses MAC address from device info)</param>
    /// <param name="broadcastAddress">Optional broadcast address</param>
    /// <param name="port">Optional WOL port</param>
    /// <returns>True if packet was sent successfully, false otherwise</returns>
    Task<bool> WakeDeviceAsync(DeviceInfo device, string? broadcastAddress = null, int port = 9);

    /// <summary>
    /// Validates a MAC address string.
    /// </summary>
    /// <param name="macAddress">MAC address to validate</param>
    /// <returns>True if valid MAC address format</returns>
    bool IsValidMacAddress(string macAddress);
}
