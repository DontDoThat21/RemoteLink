using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

public interface IAppUpdateService
{
    Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    bool ShouldCheckForUpdates(AppSettings settings, DateTimeOffset utcNow);
    void MarkChecked(AppSettings settings, DateTimeOffset utcNow);
}
