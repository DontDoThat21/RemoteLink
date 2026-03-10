using RemoteLink.Mobile.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAppLockService _appLockService;
    private readonly IncomingConnectionNotificationListener _incomingConnectionNotificationListener;
    private readonly Queue<IncomingConnectionRequestAlert> _pendingIncomingConnectionAlerts = new();
    private bool _lockPageVisible;
    private bool _isAppActive = true;
    private bool _isShowingIncomingConnectionAlert;

    public App(
        AppShell shell,
        IAppSettingsService appSettingsService,
        IAppLockService appLockService,
        IncomingConnectionNotificationListener incomingConnectionNotificationListener)
    {
        _shell = shell;
        _appSettingsService = appSettingsService;
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
}
