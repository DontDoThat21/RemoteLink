using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

public sealed class AppUpdateService : IAppUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<AppUpdateService> _logger;
    private readonly AppUpdateOptions _options;

    public AppUpdateService(HttpClient httpClient, ILogger<AppUpdateService> logger, AppUpdateOptions options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options;
    }

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.LatestReleaseApiUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(GetUserAgentProductName(), NormalizeUserAgentVersion(_options.CurrentVersion)));
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var message = $"Update check failed with HTTP {(int)response.StatusCode}.";
                _logger.LogWarning("{Message}", message);
                return new AppUpdateCheckResult
                {
                    Status = AppUpdateStatus.Failed,
                    CurrentVersion = _options.CurrentVersion,
                    Message = message,
                    ReleasePageUrl = _options.ReleasesPageUrl
                };
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new AppUpdateCheckResult
                {
                    Status = AppUpdateStatus.Failed,
                    CurrentVersion = _options.CurrentVersion,
                    Message = "Update feed returned an invalid release payload.",
                    ReleasePageUrl = _options.ReleasesPageUrl
                };
            }

            if (!TryParseVersion(_options.CurrentVersion, out var currentVersion) || !TryParseVersion(release.TagName, out var latestVersion))
            {
                return new AppUpdateCheckResult
                {
                    Status = AppUpdateStatus.Failed,
                    CurrentVersion = _options.CurrentVersion,
                    LatestVersion = release.TagName.Trim(),
                    Message = "Unable to compare the installed version with the latest release.",
                    ReleasePageUrl = release.HtmlUrl ?? _options.ReleasesPageUrl
                };
            }

            var latestVersionText = NormalizeDisplayVersion(release.TagName);
            var releasePageUrl = release.HtmlUrl ?? _options.ReleasesPageUrl;
            var downloadUrl = ResolveDownloadUrl(release);

            if (latestVersion > currentVersion)
            {
                return new AppUpdateCheckResult
                {
                    Status = AppUpdateStatus.UpdateAvailable,
                    CurrentVersion = NormalizeDisplayVersion(_options.CurrentVersion),
                    LatestVersion = latestVersionText,
                    Message = $"Update available: v{latestVersionText}.",
                    DownloadUrl = downloadUrl,
                    ReleasePageUrl = releasePageUrl,
                    ReleaseNotes = release.Body,
                    PublishedAtUtc = release.PublishedAt
                };
            }

            return new AppUpdateCheckResult
            {
                Status = AppUpdateStatus.UpToDate,
                CurrentVersion = NormalizeDisplayVersion(_options.CurrentVersion),
                LatestVersion = latestVersionText,
                Message = $"{_options.ProductName} is up to date.",
                DownloadUrl = downloadUrl,
                ReleasePageUrl = releasePageUrl,
                ReleaseNotes = release.Body,
                PublishedAtUtc = release.PublishedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates for {ProductName}", _options.ProductName);
            return new AppUpdateCheckResult
            {
                Status = AppUpdateStatus.Failed,
                CurrentVersion = NormalizeDisplayVersion(_options.CurrentVersion),
                Message = ex.Message,
                ReleasePageUrl = _options.ReleasesPageUrl
            };
        }
    }

    public bool ShouldCheckForUpdates(AppSettings settings, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Updates.EnableAutomaticChecks)
            return false;

        var intervalHours = Math.Clamp(settings.Updates.CheckIntervalHours, 1, 168);
        if (!settings.Updates.LastCheckedUtc.HasValue)
            return true;

        return utcNow - new DateTimeOffset(DateTime.SpecifyKind(settings.Updates.LastCheckedUtc.Value, DateTimeKind.Utc)) >= TimeSpan.FromHours(intervalHours);
    }

    public void MarkChecked(AppSettings settings, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Updates.LastCheckedUtc = utcNow.UtcDateTime;
    }

    private string ResolveDownloadUrl(GitHubRelease release)
    {
        var storeUrl = ResolveStoreUrl();
        if (!string.IsNullOrWhiteSpace(storeUrl))
            return storeUrl;

        var assets = release.Assets ?? [];
        var preferredAsset = _options.Platform switch
        {
            AppUpdatePlatform.DesktopWindows or AppUpdatePlatform.MobileWindows =>
                assets.FirstOrDefault(asset => EndsWithAny(asset.Name, ".appinstaller"))
                ?? assets.FirstOrDefault(asset => EndsWithAny(asset.Name, ".msixbundle", ".msix")),
            AppUpdatePlatform.MobileAndroid =>
                assets.FirstOrDefault(asset => EndsWithAny(asset.Name, ".aab", ".apk")),
            _ => null
        };

        return preferredAsset?.BrowserDownloadUrl
            ?? release.HtmlUrl
            ?? _options.ReleasesPageUrl;
    }

    private string? ResolveStoreUrl() => _options.Platform switch
    {
        AppUpdatePlatform.DesktopWindows or AppUpdatePlatform.MobileWindows => _options.WindowsStoreUrl,
        AppUpdatePlatform.MobileAndroid => _options.AndroidStoreUrl,
        AppUpdatePlatform.MobileIos => _options.IosStoreUrl,
        AppUpdatePlatform.MobileMacCatalyst => _options.MacCatalystStoreUrl,
        _ => null
    };

    private static bool EndsWithAny(string? value, params string[] suffixes)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return suffixes.Any(suffix => value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private string GetUserAgentProductName()
    {
        var sanitized = new string(_options.ProductName.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "RemoteLink" : sanitized;
    }

    private static string NormalizeUserAgentVersion(string version)
    {
        var normalized = NormalizeDisplayVersion(version);
        if (string.IsNullOrWhiteSpace(normalized))
            return "0.0.0";

        return normalized.Replace(' ', '-');
    }

    private static string NormalizeDisplayVersion(string version)
    {
        var normalized = StripVersionDecorations(version);
        return string.IsNullOrWhiteSpace(normalized) ? version.Trim() : normalized;
    }

    private static bool TryParseVersion(string version, out Version parsedVersion)
    {
        parsedVersion = new Version(0, 0, 0, 0);
        var normalized = StripVersionDecorations(version);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var components = normalized
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4)
            .ToList();

        if (components.Count == 0 || components.Any(component => !int.TryParse(component, out _)))
            return false;

        while (components.Count < 4)
            components.Add("0");

        return Version.TryParse(string.Join('.', components), out parsedVersion);
    }

    private static string StripVersionDecorations(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        var trimmed = version.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];

        var separatorIndex = trimmed.IndexOfAny(['-', '+']);
        return separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        public string? Body { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        public List<GitHubReleaseAsset>? Assets { get; set; }
    }

    private sealed class GitHubReleaseAsset
    {
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
