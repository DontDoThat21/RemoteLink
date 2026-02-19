using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Service for sending Wake-on-LAN magic packets to wake remote devices.
/// Sends UDP packets containing the magic packet format:
/// - 6 bytes of 0xFF
/// - 16 repetitions of the target MAC address (6 bytes each)
/// Total: 102 bytes sent to broadcast address on port 9 (or 7).
/// </summary>
public class WakeOnLanService : IWakeOnLanService
{
    private readonly ILogger<WakeOnLanService> _logger;
    private const int MagicPacketSize = 102;
    private const int MacAddressLength = 6;
    private const int MacRepetitions = 16;

    public WakeOnLanService(ILogger<WakeOnLanService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> SendWakePacketAsync(string macAddress, string? broadcastAddress = null, int port = 9)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                _logger.LogWarning("Cannot send Wake-on-LAN packet: MAC address is null or empty");
                return false;
            }

            if (!IsValidMacAddress(macAddress))
            {
                _logger.LogWarning("Cannot send Wake-on-LAN packet: Invalid MAC address format: {MacAddress}", macAddress);
                return false;
            }

            if (port <= 0 || port > 65535)
            {
                _logger.LogWarning("Cannot send Wake-on-LAN packet: Invalid port {Port}", port);
                return false;
            }

            // Parse MAC address bytes
            byte[] macBytes = ParseMacAddress(macAddress);

            // Build magic packet
            byte[] magicPacket = BuildMagicPacket(macBytes);

            // Send via UDP broadcast
            string targetBroadcast = broadcastAddress ?? "255.255.255.255";
            using var udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;

            await udpClient.SendAsync(magicPacket, magicPacket.Length, new IPEndPoint(IPAddress.Parse(targetBroadcast), port));

            _logger.LogInformation("Sent Wake-on-LAN magic packet to {MacAddress} via {Broadcast}:{Port}", 
                macAddress, targetBroadcast, port);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Wake-on-LAN packet to {MacAddress}", macAddress);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> WakeDeviceAsync(DeviceInfo device, string? broadcastAddress = null, int port = 9)
    {
        if (device == null)
        {
            _logger.LogWarning("Cannot wake device: Device is null");
            return false;
        }

        if (string.IsNullOrWhiteSpace(device.MacAddress))
        {
            _logger.LogWarning("Cannot wake device {DeviceName}: MAC address is not set", device.DeviceName);
            return false;
        }

        _logger.LogInformation("Waking device: {DeviceName} ({MacAddress})", device.DeviceName, device.MacAddress);
        return await SendWakePacketAsync(device.MacAddress, broadcastAddress, port);
    }

    /// <inheritdoc />
    public bool IsValidMacAddress(string macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
            return false;

        // Accept formats: XX:XX:XX:XX:XX:XX or XX-XX-XX-XX-XX-XX
        // Where XX is a hex byte (00-FF)
        var regex = new Regex(@"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$");
        return regex.IsMatch(macAddress);
    }

    /// <summary>
    /// Parses a MAC address string into 6 bytes.
    /// </summary>
    private byte[] ParseMacAddress(string macAddress)
    {
        // Remove separators (: or -)
        string cleanMac = macAddress.Replace(":", "").Replace("-", "");

        // Convert hex string to bytes
        byte[] macBytes = new byte[MacAddressLength];
        for (int i = 0; i < MacAddressLength; i++)
        {
            macBytes[i] = Convert.ToByte(cleanMac.Substring(i * 2, 2), 16);
        }

        return macBytes;
    }

    /// <summary>
    /// Builds a Wake-on-LAN magic packet.
    /// Format: 6 bytes of 0xFF + 16 repetitions of the MAC address.
    /// </summary>
    private byte[] BuildMagicPacket(byte[] macBytes)
    {
        byte[] packet = new byte[MagicPacketSize];

        // First 6 bytes: 0xFF
        for (int i = 0; i < 6; i++)
        {
            packet[i] = 0xFF;
        }

        // Next 96 bytes: MAC address repeated 16 times
        for (int i = 0; i < MacRepetitions; i++)
        {
            Array.Copy(macBytes, 0, packet, 6 + (i * MacAddressLength), MacAddressLength);
        }

        return packet;
    }
}
