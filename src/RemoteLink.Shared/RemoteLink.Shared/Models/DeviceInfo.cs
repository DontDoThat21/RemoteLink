namespace RemoteLink.Shared.Models;

/// <summary>
/// Information about a device on the network
/// </summary>
public class DeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public DeviceType Type { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsOnline { get; set; }
}

/// <summary>
/// Type of device
/// </summary>
public enum DeviceType
{
    Unknown,
    Desktop,
    Mobile
}