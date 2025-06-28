namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents a remote desktop session between a host and client
/// </summary>
public class RemoteSession
{
    public string SessionId { get; set; } = string.Empty;
    public string HostId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public SessionStatus Status { get; set; }
}

/// <summary>
/// Status of a remote desktop session
/// </summary>
public enum SessionStatus
{
    Pending,
    Connected,
    Disconnected,
    Error
}
