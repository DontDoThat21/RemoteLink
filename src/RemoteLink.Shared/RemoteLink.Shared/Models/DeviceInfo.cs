namespace RemoteLink.Shared.Models;

/// <summary>
/// Information about a device on the network
/// </summary>
public class DeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public int Port { get; set; }
    public string? PublicIPAddress { get; set; }
    public int? PublicPort { get; set; }
    public NatTraversalType NatType { get; set; }
    public List<NatEndpointCandidate> NatCandidates { get; set; } = new();
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