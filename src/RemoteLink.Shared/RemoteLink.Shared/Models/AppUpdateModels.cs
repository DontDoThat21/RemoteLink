namespace RemoteLink.Shared.Models;

public enum AppUpdatePlatform
{
    DesktopWindows,
    MobileAndroid,
    MobileIos,
    MobileMacCatalyst,
    MobileWindows
}

public enum AppUpdateStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed class AppUpdateOptions
{
    public string RepositoryOwner { get; init; } = "DontDoThat21";
    public string RepositoryName { get; init; } = "RemoteLink";
    public string ProductName { get; init; } = "RemoteLink";
    public string CurrentVersion { get; init; } = "0.0.0";
    public AppUpdatePlatform Platform { get; init; } = AppUpdatePlatform.DesktopWindows;
    public string? WindowsStoreUrl { get; init; }
    public string? AndroidStoreUrl { get; init; }
    public string? IosStoreUrl { get; init; }
    public string? MacCatalystStoreUrl { get; init; }

    public string LatestReleaseApiUrl => $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
    public string ReleasesPageUrl => $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases";
}

public sealed class AppUpdateCheckResult
{
    public AppUpdateStatus Status { get; init; }
    public string CurrentVersion { get; init; } = "0.0.0";
    public string? LatestVersion { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? DownloadUrl { get; init; }
    public string? ReleasePageUrl { get; init; }
    public string? ReleaseNotes { get; init; }
    public DateTimeOffset? PublishedAtUtc { get; init; }

    public bool UpdateAvailable => Status == AppUpdateStatus.UpdateAvailable;
    public bool CanOpenDownload => !string.IsNullOrWhiteSpace(DownloadUrl);
}
