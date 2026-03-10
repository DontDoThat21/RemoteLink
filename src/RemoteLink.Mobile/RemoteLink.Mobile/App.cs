using RemoteLink.Mobile.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAppLockService _appLockService;
    private bool _lockPageVisible;

    public App(AppShell shell, IAppSettingsService appSettingsService, IAppLockService appLockService)
    {
        _shell = shell;
        _appSettingsService = appSettingsService;
        _appLockService = appLockService;

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
        var window = new Window(_shell);
        window.Created += async (_, _) => await EnsureUnlockedAsync();
        window.Stopped += (_, _) => _appLockService.MarkBackgrounded();
        window.Resumed += async (_, _) => await EnsureUnlockedAsync();
        return window;
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
        await _appLockService.InitializeAsync();
    }

    private static void ApplyTheme(ThemeMode mode)
    {
        MainThread.BeginInvokeOnMainThread(() => ThemeColors.ApplyTheme(mode));
    }

    private async Task EnsureUnlockedAsync()
    {
        if (_lockPageVisible || _shell.Navigation.ModalStack.OfType<AppLockPage>().Any())
            return;

        if (!await _appLockService.ShouldLockAsync())
            return;

        _lockPageVisible = true;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = new AppLockPage(_appLockService);
            page.Disappearing += (_, _) => _lockPageVisible = false;
            await _shell.Navigation.PushModalAsync(page, false);
        });
    }
}
