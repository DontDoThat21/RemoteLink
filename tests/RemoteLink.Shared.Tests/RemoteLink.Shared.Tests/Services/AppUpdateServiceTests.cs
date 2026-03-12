using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Shared.Tests.Services;

public class AppUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_WhenNewerDesktopReleaseExists_ReturnsUpdateWithPreferredAsset()
    {
        var service = CreateService(
            "1.0.0",
            AppUpdatePlatform.DesktopWindows,
            """
            {
              "tag_name": "v1.1.0",
              "html_url": "https://github.com/DontDoThat21/RemoteLink/releases/tag/v1.1.0",
              "published_at": "2026-03-12T12:00:00Z",
              "assets": [
                { "name": "RemoteLink.Desktop.UI.msix", "browser_download_url": "https://example.test/desktop.msix" },
                { "name": "RemoteLink.Desktop.UI.appinstaller", "browser_download_url": "https://example.test/desktop.appinstaller" }
              ]
            }
            """);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.1.0", result.LatestVersion);
        Assert.Equal("https://example.test/desktop.appinstaller", result.DownloadUrl);
        Assert.True(result.UpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenStoreUrlConfigured_PrefersStoreLink()
    {
        var service = CreateService(
            "1.0.0",
            AppUpdatePlatform.MobileAndroid,
            """
            {
              "tag_name": "v1.2.0",
              "html_url": "https://github.com/DontDoThat21/RemoteLink/releases/tag/v1.2.0",
              "assets": [
                { "name": "RemoteLink.Mobile.apk", "browser_download_url": "https://example.test/mobile.apk" }
              ]
            }
            """,
            options => options.AndroidStoreUrl = "https://play.google.com/store/apps/details?id=com.remotelink.mobile");

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("https://play.google.com/store/apps/details?id=com.remotelink.mobile", result.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenVersionsMatch_ReturnsUpToDate()
    {
        var service = CreateService(
            "1.2",
            AppUpdatePlatform.MobileWindows,
            """
            {
              "tag_name": "v1.2.0",
              "html_url": "https://github.com/DontDoThat21/RemoteLink/releases/tag/v1.2.0",
              "assets": []
            }
            """);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateStatus.UpToDate, result.Status);
        Assert.Equal("1.2.0", result.LatestVersion);
        Assert.Equal("RemoteLink is up to date.", result.Message);
    }

    [Fact]
    public void ShouldCheckForUpdates_RespectsSettingsAndInterval()
    {
        var service = CreateService("1.0.0", AppUpdatePlatform.DesktopWindows, "{}" );
        var settings = new AppSettings();
        var now = new DateTimeOffset(2026, 03, 12, 14, 0, 0, TimeSpan.Zero);

        Assert.True(service.ShouldCheckForUpdates(settings, now));

        settings.Updates.EnableAutomaticChecks = false;
        Assert.False(service.ShouldCheckForUpdates(settings, now));

        settings.Updates.EnableAutomaticChecks = true;
        settings.Updates.LastCheckedUtc = now.UtcDateTime.AddHours(-2);
        settings.Updates.CheckIntervalHours = 24;
        Assert.False(service.ShouldCheckForUpdates(settings, now));

        settings.Updates.LastCheckedUtc = now.UtcDateTime.AddHours(-25);
        Assert.True(service.ShouldCheckForUpdates(settings, now));

        service.MarkChecked(settings, now);
        Assert.Equal(now.UtcDateTime, settings.Updates.LastCheckedUtc);
    }

    private static AppUpdateService CreateService(
        string currentVersion,
        AppUpdatePlatform platform,
        string json,
        Action<AppUpdateOptions>? mutate = null)
    {
        var options = new AppUpdateOptions
        {
            ProductName = "RemoteLink",
            CurrentVersion = currentVersion,
            Platform = platform
        };

        mutate?.Invoke(options);

        var httpClient = new HttpClient(new StubHttpMessageHandler(json))
        {
            BaseAddress = new Uri("https://example.test/")
        };

        return new AppUpdateService(httpClient, NullLogger<AppUpdateService>.Instance, options);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHttpMessageHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
