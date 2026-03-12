using RemoteLink.Shared.Services;

namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents an active outgoing remote-control session.
/// </summary>
public sealed class RemoteClientSession
{
    public required string SessionId { get; init; }

    public required DeviceInfo Host { get; init; }

    public required RemoteDesktopClient Client { get; init; }

    public DateTime ConnectedAtUtc { get; init; } = DateTime.UtcNow;

    public string DisplayName => Host.DeviceName;
}
