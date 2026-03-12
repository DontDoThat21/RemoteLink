using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RemoteLink.Desktop.UI.Services;
using RemoteLink.Shared.Interfaces;

namespace RemoteLink.Desktop.UI;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly ILogger<App> _logger;
    private readonly WindowsSystemTrayService _trayService;
    private readonly IAppSettingsService _appSettings;
    private readonly IAppUpdateService _appUpdateService;
    private Window? _mainWindow;
    private NavigationPage? _navPage;
    private bool _isQuitting;
    private readonly bool _startMinimized;
    private bool _isCheckingForUpdates;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    public App(
        MainPage mainPage,
        ILogger<App> logger,
        WindowsSystemTrayService trayService,
        IAppSettingsService appSettings,
        IAppUpdateService appUpdateService)
    {
        _mainPage = mainPage;
        _logger = logger;
        _trayService = trayService;
        _appSettings = appSettings;
        _appUpdateService = appUpdateService;

        // Check for --minimized command-line flag (used by auto-start / startup task)
        var args = Environment.GetCommandLineArgs();
        _startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                       || _appSettings.Current.Startup.StartMinimized;

        _trayService.ShowWindowRequested += OnTrayShowRequested;
        _trayService.QuitRequested += OnTrayQuitRequested;
        _trayService.Initialize();

        // Apply initial theme from settings
        ThemeColors.ApplyTheme(_appSettings.Current.General.Theme);

        // Re-apply theme when settings change
        _appSettings.SettingsSaved += (_, _) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
                ThemeColors.ApplyTheme(_appSettings.Current.General.Theme));
        };

        // Re-evaluate when OS theme changes (for System mode)
        RequestedThemeChanged += (_, _) =>
        {
            if (_appSettings.Current.General.Theme == Shared.Models.ThemeMode.System)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    ThemeColors.ApplyTheme(Shared.Models.ThemeMode.System));
            }
        };

        // Update nav bar colors when theme changes
        ThemeColors.ThemeChanged += OnThemeChanged;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _navPage = new NavigationPage(_mainPage)
        {
            BarBackgroundColor = ThemeColors.NavBarBackground,
            BarTextColor = ThemeColors.TextOnDark
        };

        _mainWindow = new Window(_navPage);

        _mainWindow.Title = "RemoteLink Desktop";
        _mainWindow.Width = 780;
        _mainWindow.Height = 680;
        _mainWindow.MinimumWidth = 640;
        _mainWindow.MinimumHeight = 560;

        // Intercept window close to minimize to tray instead
        _mainWindow.Destroying += OnWindowDestroying;

        // If --minimized flag or StartMinimized setting, hide to tray after window renders
        if (_startMinimized)
        {
            _mainWindow.Created += (_, _) =>
            {
                // Delay slightly to let the native window initialize
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(200);
                    HideMainWindow();
                });
            };
        }

        _mainWindow.Created += async (_, _) => await CheckForUpdatesIfNeededAsync();

        return _mainWindow;
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_navPage != null)
            {
                _navPage.BarBackgroundColor = ThemeColors.NavBarBackground;
                _navPage.BarTextColor = ThemeColors.TextOnDark;
            }
        });
    }

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        if (!_isQuitting)
        {
            // Hide instead of close — minimize to tray
            HideMainWindow();
        }
    }

    /// <summary>
    /// Gets the native HWND for the main MAUI window.
    /// </summary>
    private IntPtr GetNativeWindowHandle()
    {
        if (_mainWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
        {
            return WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        }
        return IntPtr.Zero;
    }

    private void HideMainWindow()
    {
        var hwnd = GetNativeWindowHandle();
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, SW_HIDE);
    }

    private void RestoreMainWindow()
    {
        var hwnd = GetNativeWindowHandle();
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SW_SHOW);
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
    }

    private void OnTrayShowRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RestoreMainWindow);
    }

    private void OnTrayQuitRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _isQuitting = true;
            _trayService.Dispose();
            Current?.Quit();
        });
    }

    private async Task CheckForUpdatesIfNeededAsync()
    {
        if (_isCheckingForUpdates)
            return;

        if (!_appUpdateService.ShouldCheckForUpdates(_appSettings.Current, DateTimeOffset.UtcNow))
            return;

        _isCheckingForUpdates = true;
        try
        {
            var result = await _appUpdateService.CheckForUpdatesAsync();
            if (result.Status != RemoteLink.Shared.Models.AppUpdateStatus.Failed)
            {
                _appUpdateService.MarkChecked(_appSettings.Current, DateTimeOffset.UtcNow);
                await _appSettings.SaveAsync();
            }

            if (!result.UpdateAvailable || !result.CanOpenDownload)
                return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = _navPage?.CurrentPage ?? _mainPage;
                var shouldOpen = await page.DisplayAlertAsync(
                    "Update Available",
                    $"RemoteLink Desktop v{result.LatestVersion} is available. Open the update page now?",
                    "Open",
                    "Later");

                if (shouldOpen && Uri.TryCreate(result.DownloadUrl, UriKind.Absolute, out var uri))
                    await Launcher.Default.OpenAsync(uri);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic update check failed");
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }
}
