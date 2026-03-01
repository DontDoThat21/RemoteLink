using System.Runtime.InteropServices;

namespace RemoteLink.Desktop.UI.Services;

/// <summary>
/// Windows system tray (notification area) icon service using Shell_NotifyIconW P/Invoke.
/// Provides minimize-to-tray, context menu (status, connections, quit), and double-click restore.
/// </summary>
public sealed class WindowsSystemTrayService : IDisposable
{
    // ── Shell_NotifyIcon constants ──────────────────────────────────────
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    // ── Window message constants ───────────────────────────────────────
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 88;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_COMMAND = 0x0111;
    private const int WM_DESTROY = 0x0002;
    private const int WM_CLOSE = 0x0010;

    // ── Context menu item IDs ──────────────────────────────────────────
    private const int IDM_SHOW = 1001;
    private const int IDM_STATUS = 1002;
    private const int IDM_CONNECTIONS = 1003;
    private const int IDM_SEPARATOR = 1004;
    private const int IDM_QUIT = 1005;

    // ── Menu flags ─────────────────────────────────────────────────────
    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_ENABLED = 0x00000000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;

    // ── Window class/creation constants ────────────────────────────────
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private const int CS_HREDRAW = 0x0002;
    private const int CS_VREDRAW = 0x0001;

    // ── Icon constants ─────────────────────────────────────────────────
    private const int IDI_APPLICATION = 32512;

    // ── P/Invoke declarations ──────────────────────────────────────────

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InsertMenuW(IntPtr hMenu, uint uPosition, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandleW(IntPtr lpModuleName);

    // ── Instance state ─────────────────────────────────────────────────

    private IntPtr _messageHwnd;
    private IntPtr _iconHandle;
    private WndProcDelegate? _wndProc;   // prevent GC collection of delegate
    private GCHandle _wndProcHandle;
    private bool _iconAdded;
    private bool _disposed;
    private string _tooltipText = "RemoteLink Desktop";
    private string _statusText = "Stopped";
    private int _connectionCount;

    /// <summary>Raised when the user double-clicks the tray icon or selects "Show RemoteLink".</summary>
    public event EventHandler? ShowWindowRequested;

    /// <summary>Raised when the user selects "Quit" from the tray context menu.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>
    /// Initializes the system tray icon. Call once during app startup.
    /// </summary>
    public void Initialize()
    {
        if (_messageHwnd != IntPtr.Zero) return;

        _iconHandle = LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
        CreateMessageWindow();
        AddTrayIcon();
    }

    /// <summary>
    /// Updates the tray tooltip and internal status text shown in the context menu.
    /// </summary>
    public void UpdateStatus(string statusText, int connectionCount)
    {
        _statusText = statusText;
        _connectionCount = connectionCount;
        _tooltipText = connectionCount > 0
            ? $"RemoteLink — {statusText} ({connectionCount} connection{(connectionCount != 1 ? "s" : "")})"
            : $"RemoteLink — {statusText}";

        if (_tooltipText.Length > 127)
            _tooltipText = _tooltipText[..127];

        if (_iconAdded)
            ModifyTrayIcon();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        RemoveTrayIcon();

        if (_messageHwnd != IntPtr.Zero)
        {
            DestroyWindow(_messageHwnd);
            _messageHwnd = IntPtr.Zero;
        }

        if (_wndProcHandle.IsAllocated)
            _wndProcHandle.Free();

        if (_iconHandle != IntPtr.Zero)
            _iconHandle = IntPtr.Zero; // LoadIcon icons from system don't need DestroyIcon
    }

    // ── Tray icon management ───────────────────────────────────────────

    private void AddTrayIcon()
    {
        var nid = CreateNotifyIconData();
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        nid.uCallbackMessage = WM_TRAYICON;
        nid.hIcon = _iconHandle;
        nid.szTip = _tooltipText;

        _iconAdded = Shell_NotifyIconW(NIM_ADD, ref nid);
    }

    private void ModifyTrayIcon()
    {
        var nid = CreateNotifyIconData();
        nid.uFlags = NIF_TIP;
        nid.szTip = _tooltipText;

        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    private void RemoveTrayIcon()
    {
        if (!_iconAdded) return;

        var nid = CreateNotifyIconData();
        Shell_NotifyIconW(NIM_DELETE, ref nid);
        _iconAdded = false;
    }

    private NOTIFYICONDATAW CreateNotifyIconData()
    {
        return new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _messageHwnd,
            uID = 1,
            szTip = _tooltipText
        };
    }

    // ── Message-only window ────────────────────────────────────────────

    private void CreateMessageWindow()
    {
        _wndProc = WndProc;
        _wndProcHandle = GCHandle.Alloc(_wndProc);

        var hInstance = GetModuleHandleW(IntPtr.Zero);
        var className = "RemoteLinkTrayMsg_" + Environment.ProcessId;

        var wc = new WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
            style = CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = className
        };

        RegisterClassExW(ref wc);

        _messageHwnd = CreateWindowExW(
            0, className, "RemoteLink Tray",
            0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            int eventId = (int)(lParam.ToInt64() & 0xFFFF);

            if (eventId == WM_LBUTTONDBLCLK)
            {
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (eventId == WM_RBUTTONUP)
            {
                ShowContextMenu();
            }
        }
        else if (msg == WM_COMMAND)
        {
            int menuId = (int)(wParam.ToInt64() & 0xFFFF);
            HandleMenuCommand(menuId);
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // ── Context menu ───────────────────────────────────────────────────

    private void ShowContextMenu()
    {
        var hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            // "Show RemoteLink" — always enabled
            InsertMenuW(hMenu, 0, MF_STRING | MF_ENABLED, (IntPtr)IDM_SHOW, "Show RemoteLink");

            // Separator
            InsertMenuW(hMenu, 1, MF_SEPARATOR, IntPtr.Zero, null);

            // Status (grayed info line)
            InsertMenuW(hMenu, 2, MF_STRING | MF_GRAYED, (IntPtr)IDM_STATUS,
                $"Status: {_statusText}");

            // Connections (grayed info line)
            var connText = _connectionCount > 0
                ? $"Connections: {_connectionCount}"
                : "Connections: None";
            InsertMenuW(hMenu, 3, MF_STRING | MF_GRAYED, (IntPtr)IDM_CONNECTIONS, connText);

            // Separator
            InsertMenuW(hMenu, 4, MF_SEPARATOR, IntPtr.Zero, null);

            // Quit
            InsertMenuW(hMenu, 5, MF_STRING | MF_ENABLED, (IntPtr)IDM_QUIT, "Quit RemoteLink");

            // Required per MSDN: SetForegroundWindow before TrackPopupMenu
            SetForegroundWindow(_messageHwnd);

            GetCursorPos(out POINT pt);
            int cmd = TrackPopupMenuEx(hMenu,
                TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                pt.X, pt.Y, _messageHwnd, IntPtr.Zero);

            if (cmd != 0)
                HandleMenuCommand(cmd);
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private void HandleMenuCommand(int menuId)
    {
        switch (menuId)
        {
            case IDM_SHOW:
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                break;
            case IDM_QUIT:
                QuitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
