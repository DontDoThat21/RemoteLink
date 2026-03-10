namespace RemoteLink.Shared.Models;

/// <summary>
/// Information about a device on the network
/// </summary>
public class DeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string? InternetDeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public int Port { get; set; }
    public string? PublicIPAddress { get; set; }
    public int? PublicPort { get; set; }
    public NatTraversalType NatType { get; set; }
    public List<NatEndpointCandidate> NatCandidates { get; set; } = new();
    public bool SupportsRelay { get; set; }
    public string? RelayServerHost { get; set; }
    public int? RelayServerPort { get; set; }
    public bool SupportsSecureTunnel { get; set; }
    public bool RequiresSecureTunnel { get; set; }
    public bool SupportsPresentationMode { get; set; }
    public bool PresentationSessionActive { get; set; }
    public int? PresentationPort { get; set; }
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