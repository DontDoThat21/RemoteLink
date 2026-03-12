using RemoteLink.Shared.Models;
using Xunit;

namespace RemoteLink.Shared.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void Constructor_InitializesAllSections()
    {
        var settings = new AppSettings();

        Assert.NotNull(settings.General);
        Assert.NotNull(settings.Security);
        Assert.NotNull(settings.Network);
        Assert.NotNull(settings.Display);
        Assert.NotNull(settings.Input);
        Assert.NotNull(settings.Audio);
        Assert.NotNull(settings.Recording);
        Assert.NotNull(settings.Updates);
        Assert.NotNull(settings.Startup);
    }

    [Fact]
    public void Constructor_UsesExpectedMobilePreferenceDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal(ThemeMode.System, settings.General.Theme);
        Assert.False(settings.Security.EnableAppLock);
        Assert.Equal(1, settings.Security.AppLockTimeoutMinutes);
        Assert.Equal(0, settings.Security.IdleDisconnectMinutes);
        Assert.True(settings.Display.EnableAdaptiveQuality);
        Assert.Equal(ImageFormat.Jpeg, settings.Display.ImageFormat);
        Assert.Equal(80, settings.Display.ImageQuality);
        Assert.Equal(1.0, settings.Input.GestureSensitivity, 3);
        Assert.True(settings.General.ShowConnectionNotifications);
        Assert.False(settings.Audio.EnableAudio);
        Assert.True(settings.Updates.EnableAutomaticChecks);
        Assert.Equal(24, settings.Updates.CheckIntervalHours);
        Assert.Null(settings.Updates.LastCheckedUtc);
    }
}
