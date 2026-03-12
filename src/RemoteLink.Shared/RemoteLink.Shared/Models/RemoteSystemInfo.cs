namespace RemoteLink.Shared.Models;

/// <summary>
/// Point-in-time snapshot describing the connected remote host.
/// </summary>
public class RemoteSystemInfo
{
    public string MachineName { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string OsArchitecture { get; set; } = string.Empty;
    public string FrameworkDescription { get; set; } = string.Empty;
    public string ProcessorName { get; set; } = string.Empty;
    public int LogicalProcessorCount { get; set; }
    public long TotalMemoryBytes { get; set; }
    public long AvailableMemoryBytes { get; set; }
    public long UptimeSeconds { get; set; }
    public List<RemoteDiskInfo> Disks { get; set; } = [];
    public List<RemoteNetworkInterfaceInfo> NetworkInterfaces { get; set; } = [];
}

/// <summary>
/// Storage summary for a remote drive.
/// </summary>
public class RemoteDiskInfo
{
    public string Name { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public string DriveFormat { get; set; } = string.Empty;
    public string DriveType { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public long AvailableFreeSpaceBytes { get; set; }
}

/// <summary>
/// Network adapter summary for a remote host.
/// </summary>
public class RemoteNetworkInterfaceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public string OperationalStatus { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public List<string> IPv4Addresses { get; set; } = [];
    public List<string> IPv6Addresses { get; set; } = [];
}