namespace RemoteLink.Shared.Models;

/// <summary>
/// Outcome of a remote connection attempt.
/// </summary>
public enum ConnectionOutcome
{
    Success,
    Failed,
    Disconnected,
    Error
}

/// <summary>
/// A persisted record of a past remote connection session.
/// </summary>
public class ConnectionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DisconnectedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public ConnectionOutcome Outcome { get; set; } = ConnectionOutcome.Success;
}
