using RemoteLink.Mobile.Services;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly ILogger<App> _logger;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAppUpdateService _appUpdateService;
    private readonly IAppLockService _appLockService;
    private readonly IncomingConnectionNotificationListener _incomingConnectionNotificationListener;
    private readonly Queue<IncomingConnectionRequestAlert> _pendingIncomingConnectionAlerts = new();
    private AppUpdateCheckResult? _pendingUpdatePrompt;
    private bool _lockPageVisible;
    private bool _isAppActive = true;
    private bool _isShowingIncomingConnectionAlert;
    private bool _isShowingUpdatePrompt;
    private bool _isCheckingForUpdates;

    public App(
        AppShell shell,
        ILogger<App> logger,
        IAppSettingsService appSettingsService,
        IAppUpdateService appUpdateService,
        IAppLockService appLockService,
        IncomingConnectionNotificationListener incomingConnectionNotificationListener)
    {
        _shell = shell;
        _logger = logger;
        _appSettingsService = appSettingsService;
        _appUpdateService = appUpdateService;
        _appLockService = appLockService;
        _incomingConnectionNotificationListener = incomingConnectionNotificationListener;

        _appSettingsService.SettingsSaved += (_, _) => ApplyTheme(_appSettingsService.Current.General.Theme);
        _incomingConnectionNotificationListener.NotificationReceived += OnIncomingConnectionNotificationReceived;
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
        window.Created += async (_, _) =>
        {
            _isAppActive = true;
            await _incomingConnectionNotificationListener.StartAsync();
            await EnsureUnlockedAsync();
            await CheckForUpdatesIfNeededAsync();
            await ShowPendingUpdatePromptAsync();
            await ShowPendingIncomingConnectionAlertsAsync();
        };
        window.Stopped += (_, _) =>
        {
            _isAppActive = false;
            _appLockService.MarkBackgrounded();
        };
        window.Resumed += async (_, _) =>
        {
            _isAppActive = true;
            await EnsureUnlockedAsync();
            await CheckForUpdatesIfNeededAsync();
            await ShowPendingUpdatePromptAsync();
            await ShowPendingIncomingConnectionAlertsAsync();
        };
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
        await _incomingConnectionNotificationListener.StartAsync();
        await CheckForUpdatesIfNeededAsync();
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

    private void OnIncomingConnectionNotificationReceived(object? sender, IncomingConnectionRequestAlert alert)
    {
        lock (_pendingIncomingConnectionAlerts)
        {
            if (!_isAppActive || _lockPageVisible || _shell.Navigation.ModalStack.OfType<AppLockPage>().Any())
            {
                _pendingIncomingConnectionAlerts.Enqueue(alert);
                return;
            }
        }

        _ = ShowIncomingConnectionAlertAsync(alert);
    }

    private async Task ShowPendingIncomingConnectionAlertsAsync()
    {
        while (true)
        {
            IncomingConnectionRequestAlert? alert;
            lock (_pendingIncomingConnectionAlerts)
            {
                if (_pendingIncomingConnectionAlerts.Count == 0 || !_isAppActive || _lockPageVisible)
                    return;

                alert = _pendingIncomingConnectionAlerts.Dequeue();
            }

            await ShowIncomingConnectionAlertAsync(alert);
        }
    }

    private async Task ShowIncomingConnectionAlertAsync(IncomingConnectionRequestAlert alert)
    {
        if (_isShowingIncomingConnectionAlert)
        {
            lock (_pendingIncomingConnectionAlerts)
            {
                _pendingIncomingConnectionAlerts.Enqueue(alert);
            }
            return;
        }

        _isShowingIncomingConnectionAlert = true;

        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (!_isAppActive || _lockPageVisible || _shell.Navigation.ModalStack.OfType<AppLockPage>().Any())
                {
                    lock (_pendingIncomingConnectionAlerts)
                    {
                        _pendingIncomingConnectionAlerts.Enqueue(alert);
                    }
                    return;
                }

                var page = Application.Current?.Windows.FirstOrDefault()?.Page ?? _shell;
                var requester = string.IsNullOrWhiteSpace(alert.ClientDeviceName) ? "Another device" : alert.ClientDeviceName;
                var host = string.IsNullOrWhiteSpace(alert.HostDeviceName) ? "your RemoteLink host" : alert.HostDeviceName;

                await page.DisplayAlertAsync(
                    "Incoming Connection Request",
                    $"{requester} wants to connect to {host}.",
                    "OK");
            });
        }
        finally
        {
            _isShowingIncomingConnectionAlert = false;
        }
    }

    private async Task CheckForUpdatesIfNeededAsync()
    {
        if (_isCheckingForUpdates)
            return;

        if (!_appUpdateService.ShouldCheckForUpdates(_appSettingsService.Current, DateTimeOffset.UtcNow))
            return;

        _isCheckingForUpdates = true;
        try
        {
            var result = await _appUpdateService.CheckForUpdatesAsync();
            if (result.Status != AppUpdateStatus.Failed)
            {
                _appUpdateService.MarkChecked(_appSettingsService.Current, DateTimeOffset.UtcNow);
                await _appSettingsService.SaveAsync();
            }

            if (!result.UpdateAvailable || !result.CanOpenDownload)
                return;

            if (!_isAppActive || _lockPageVisible || _shell.Navigation.ModalStack.OfType<AppLockPage>().Any())
            {
                _pendingUpdatePrompt = result;
                return;
            }

            await ShowUpdatePromptAsync(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic mobile update check failed");
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    private async Task ShowPendingUpdatePromptAsync()
    {
        if (_pendingUpdatePrompt == null)
            return;

        var pending = _pendingUpdatePrompt;
        _pendingUpdatePrompt = null;
        await ShowUpdatePromptAsync(pending);
    }

    private async Task ShowUpdatePromptAsync(AppUpdateCheckResult result)
    {
        if (_isShowingUpdatePrompt || !_isAppActive || _lockPageVisible || !result.CanOpenDownload)
        {
            _pendingUpdatePrompt = result;
            return;
        }

        _isShowingUpdatePrompt = true;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (!_isAppActive || _lockPageVisible || _shell.Navigation.ModalStack.OfType<AppLockPage>().Any())
                {
                    _pendingUpdatePrompt = result;
                    return;
                }

                var page = Application.Current?.Windows.FirstOrDefault()?.Page ?? _shell;
                var shouldOpen = await page.DisplayAlertAsync(
                    "Update Available",
                    $"RemoteLink Mobile v{result.LatestVersion} is available. Open the update page now?",
                    "Open",
                    "Later");

                if (shouldOpen && Uri.TryCreate(result.DownloadUrl, UriKind.Absolute, out var uri))
                    await Launcher.Default.OpenAsync(uri);
            });
        }
        finally
        {
            _isShowingUpdatePrompt = false;
        }
    }
}
