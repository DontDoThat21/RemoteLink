namespace RemoteLink.Shared.Models;

/// <summary>
/// A device saved to the user's address book for quick reconnection.
/// </summary>
public class SavedDevice
{
    /// <summary>Unique identifier for this saved entry (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>User-assigned friendly name (e.g. "Office PC", "Living Room").</summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>Original device name from discovery.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Network device ID from discovery.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Globally unique 9-digit internet ID when available.</summary>
    public string? InternetDeviceId { get; set; }

    /// <summary>Last known IP address.</summary>
    public string IPAddress { get; set; } = string.Empty;

    /// <summary>Last known port.</summary>
    public int Port { get; set; }

    /// <summary>Whether the device supports relay-based internet connections.</summary>
    public bool SupportsRelay { get; set; }

    /// <summary>The last advertised relay server host.</summary>
    public string? RelayServerHost { get; set; }

    /// <summary>The last advertised relay server port.</summary>
    public int? RelayServerPort { get; set; }

    /// <summary>Device type (Desktop / Mobile).</summary>
    public DeviceType Type { get; set; } = DeviceType.Desktop;

    /// <summary>When this device was last successfully connected to.</summary>
    public DateTime? LastConnected { get; set; }

    /// <summary>When this entry was first saved.</summary>
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
