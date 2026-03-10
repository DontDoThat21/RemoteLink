using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly IAppSettingsService _appSettingsService;

    public App(AppShell shell, IAppSettingsService appSettingsService)
    {
        _shell = shell;
        _appSettingsService = appSettingsService;

        _appSettingsService.SettingsSaved += (_, _) => ApplyTheme(_appSettingsService.Current.General.Theme);
        RequestedThemeChanged += (_, _) =>
        {
            if (_appSettingsService.Current.General.Theme == ThemeMode.System)
                ApplyTheme(ThemeMode.System);
        };

        _ = InitializeThemeAsync();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_shell);
    }

    private async Task InitializeThemeAsync()
    {
        try
        {
            await _appSettingsService.LoadAsync();
        }
        catch
        {
            // Fall back to defaults if persisted settings cannot be loaded.
        }

        ApplyTheme(_appSettingsService.Current.General.Theme);
    }

    private static void ApplyTheme(ThemeMode mode)
    {
        MainThread.BeginInvokeOnMainThread(() => ThemeColors.ApplyTheme(mode));
    }
}
