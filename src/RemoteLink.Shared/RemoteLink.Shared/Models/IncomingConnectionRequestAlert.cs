namespace RemoteLink.Shared.Models;

/// <summary>
/// Lightweight LAN notification payload published when a host receives a pairing attempt.
/// </summary>
public class IncomingConnectionRequestAlert
{
    /// <summary>
    /// Unique identifier for this alert instance.
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Host machine name receiving the connection attempt.
    /// </summary>
    public string HostDeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Remote client device identifier from the pairing request.
    /// </summary>
    public string ClientDeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Remote client device name from the pairing request.
    /// </summary>
    public string ClientDeviceName { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the pairing attempt was made.
    /// </summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Protocol constants for incoming connection request LAN notifications.
/// </summary>
public static class IncomingConnectionRequestAlertProtocol
{
    /// <summary>
    /// UDP port used to broadcast incoming connection request alerts.
    /// </summary>
    public const int Port = 12348;
}
