using System.Runtime.InteropServices;
using RemoteLink.Desktop.UI.Services;

namespace RemoteLink.Desktop.UI;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly WindowsSystemTrayService _trayService;
    private Window? _mainWindow;
    private bool _isQuitting;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    public App(MainPage mainPage, WindowsSystemTrayService trayService)
    {
        _mainPage = mainPage;
        _trayService = trayService;

        _trayService.ShowWindowRequested += OnTrayShowRequested;
        _trayService.QuitRequested += OnTrayQuitRequested;
        _trayService.Initialize();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _mainWindow = new Window(new NavigationPage(_mainPage)
        {
            BarBackgroundColor = Color.FromArgb("#512BD4"),
            BarTextColor = Colors.White
        });

        _mainWindow.Title = "RemoteLink Desktop";
        _mainWindow.Width = 780;
        _mainWindow.Height = 680;
        _mainWindow.MinimumWidth = 640;
        _mainWindow.MinimumHeight = 560;

        // Intercept window close to minimize to tray instead
        _mainWindow.Destroying += OnWindowDestroying;

        return _mainWindow;
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
}
