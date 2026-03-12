using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Provides a point-in-time snapshot of the current host system.
/// </summary>
public interface IRemoteSystemInfoProvider
{
    Task<RemoteSystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken = default);
}