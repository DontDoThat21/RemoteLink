using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Interface for network discovery of devices
/// </summary>
public interface INetworkDiscovery
{
    /// <summary>
    /// Start broadcasting device presence on the network
    /// </summary>
    Task StartBroadcastingAsync();

    /// <summary>
    /// Stop broadcasting device presence
    /// </summary>
    Task StopBroadcastingAsync();

    /// <summary>
    /// Start listening for devices on the network
    /// </summary>
    Task StartListeningAsync();

    /// <summary>
    /// Stop listening for devices
    /// </summary>
    Task StopListeningAsync();

    /// <summary>
    /// Get all discovered devices
    /// </summary>
    Task<IEnumerable<DeviceInfo>> GetDiscoveredDevicesAsync();

    /// <summary>
    /// Event fired when a new device is discovered
    /// </summary>
    event EventHandler<DeviceInfo> DeviceDiscovered;

    /// <summary>
    /// Event fired when a device goes offline
    /// </summary>
    event EventHandler<DeviceInfo> DeviceLost;
}