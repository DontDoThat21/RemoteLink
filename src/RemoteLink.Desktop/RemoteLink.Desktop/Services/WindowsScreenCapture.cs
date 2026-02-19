using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Windows screen capture using BitBlt / GDI P/Invoke.
/// Captures the primary monitor or a specific selected monitor as 32-bit BGRA pixel data at up to 10 FPS.
///
/// Platform guards are applied at runtime so this class compiles on Linux;
/// it is only instantiated on Windows (see Program.cs DI wiring).
/// On non-Windows the capture methods return empty/fallback data safely.
/// </summary>
public sealed class WindowsScreenCapture : IScreenCapture, IDisposable
{
    private readonly ILogger<WindowsScreenCapture> _logger;
    private readonly object _monitorLock = new();
    private Timer? _captureTimer;
    private int _quality = 75;
    private bool _isCapturing;
    private string? _selectedMonitorId;
    private List<MonitorInfo> _cachedMonitors = new();

    /// <summary>Exposed for test inspection (not on IScreenCapture).</summary>
    public bool IsCapturing => _isCapturing;

    public event EventHandler<ScreenData>? FrameCaptured;

    public WindowsScreenCapture(ILogger<WindowsScreenCapture> logger)
    {
        _logger = logger;
    }

    // ── IScreenCapture ─────────────────────────────────────────────────────────

    public Task StartCaptureAsync()
    {
        if (_isCapturing) return Task.CompletedTask;

        _isCapturing = true;
        _captureTimer = new Timer(CaptureFrame, null, 0, 100); // 10 FPS
        _logger.LogInformation("WindowsScreenCapture: started");
        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        _isCapturing = false;
        _captureTimer?.Dispose();
        _captureTimer = null;
        _logger.LogInformation("WindowsScreenCapture: stopped");
        return Task.CompletedTask;
    }

    public async Task<ScreenData> CaptureFrameAsync()
    {
        var (width, height) = await GetScreenDimensionsAsync();
        var imageData = CaptureScreenBits(width, height);

        return new ScreenData
        {
            ImageData = imageData,
            Width = width,
            Height = height,
            Format = ScreenDataFormat.Raw,
            Quality = _quality
        };
    }

    public async Task<(int Width, int Height)> GetScreenDimensionsAsync()
    {
        if (!OperatingSystem.IsWindows())
            return (1920, 1080); // safe fallback for non-Windows

        string? selectedId;
        lock (_monitorLock)
        {
            selectedId = _selectedMonitorId;
        }

        // If a specific monitor is selected, return its dimensions
        if (!string.IsNullOrEmpty(selectedId))
        {
            var monitors = await GetMonitorsAsync();
            var selected = monitors.FirstOrDefault(m => m.Id == selectedId);
            if (selected != null)
            {
                return (selected.Width, selected.Height);
            }
        }

        // Default to primary monitor dimensions
        int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        if (w <= 0) w = 1920;
        if (h <= 0) h = 1080;

        return (w, h);
    }

    public void SetQuality(int quality)
        => _quality = Math.Clamp(quality, 1, 100);

    public Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Return a single mock monitor for non-Windows platforms
            var mockMonitor = new MonitorInfo
            {
                Id = "primary",
                Name = "Primary Display",
                IsPrimary = true,
                Width = 1920,
                Height = 1080,
                Left = 0,
                Top = 0
            };
            return Task.FromResult<IReadOnlyList<MonitorInfo>>(new[] { mockMonitor });
        }

        lock (_monitorLock)
        {
            _cachedMonitors.Clear();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumProc, IntPtr.Zero);
            return Task.FromResult<IReadOnlyList<MonitorInfo>>(_cachedMonitors.ToList());
        }
    }

    public Task<bool> SelectMonitorAsync(string monitorId)
    {
        lock (_monitorLock)
        {
            _selectedMonitorId = monitorId;
            _logger.LogInformation("WindowsScreenCapture: selected monitor {MonitorId}", monitorId);
            return Task.FromResult(true);
        }
    }

    public string? GetSelectedMonitorId()
    {
        lock (_monitorLock)
        {
            return _selectedMonitorId;
        }
    }

    // ── Internal capture logic ─────────────────────────────────────────────────

    /// <summary>
    /// Callback for EnumDisplayMonitors. Populates _cachedMonitors.
    /// </summary>
    private bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData)
    {
        if (!OperatingSystem.IsWindows())
            return true;

        var info = new NativeMethods.MONITORINFOEX();
        info.cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>();

        if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            var monitorInfo = new MonitorInfo
            {
                Id = hMonitor.ToString(),
                Name = info.szDevice,
                IsPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                Width = info.rcMonitor.Right - info.rcMonitor.Left,
                Height = info.rcMonitor.Bottom - info.rcMonitor.Top,
                Left = info.rcMonitor.Left,
                Top = info.rcMonitor.Top
            };

            _cachedMonitors.Add(monitorInfo);
        }

        return true; // continue enumeration
    }

    /// <summary>
    /// Uses BitBlt to blit the desktop (or a specific monitor) to an off-screen DC, 
    /// then GetDIBits to extract raw 32-bit BGRA pixel data. Returns an empty array 
    /// on failure or when running on a non-Windows platform.
    /// </summary>
    private byte[] CaptureScreenBits(int width, int height)
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<byte>();

        // Determine source coordinates (for specific monitor capture)
        int srcX = 0, srcY = 0;
        string? selectedId;
        lock (_monitorLock)
        {
            selectedId = _selectedMonitorId;
        }

        if (!string.IsNullOrEmpty(selectedId))
        {
            var monitors = GetMonitorsAsync().Result;
            var selected = monitors.FirstOrDefault(m => m.Id == selectedId);
            if (selected != null)
            {
                srcX = selected.Left;
                srcY = selected.Top;
            }
        }

        IntPtr hDesktop = NativeMethods.GetDesktopWindow();
        IntPtr hDC     = NativeMethods.GetDC(hDesktop);

        if (hDC == IntPtr.Zero)
        {
            _logger.LogWarning("WindowsScreenCapture: GetDC returned null");
            return Array.Empty<byte>();
        }

        IntPtr hMemDC  = NativeMethods.CreateCompatibleDC(hDC);
        IntPtr hBitmap = NativeMethods.CreateCompatibleBitmap(hDC, width, height);
        IntPtr hOld    = NativeMethods.SelectObject(hMemDC, hBitmap);

        try
        {
            bool blitOk = NativeMethods.BitBlt(
                hMemDC, 0, 0, width, height,
                hDC,    srcX, srcY,
                NativeMethods.SRCCOPY);

            if (!blitOk)
            {
                _logger.LogWarning("WindowsScreenCapture: BitBlt failed (error={Error})",
                    Marshal.GetLastWin32Error());
                return Array.Empty<byte>();
            }

            // Build a top-down DIB header (negative biHeight = top-down)
            var bmi = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize        = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth       = width,
                    biHeight      = -height, // negative → top-down scanlines
                    biPlanes      = 1,
                    biBitCount    = 32,
                    biCompression = 0,       // BI_RGB
                    biSizeImage   = 0
                }
            };

            // stride = width * 4 bytes (BGRA, 32-bit, no padding at 32 bpp)
            byte[] pixels = new byte[width * 4 * height];
            int result = NativeMethods.GetDIBits(
                hMemDC, hBitmap, 0, (uint)height, pixels, ref bmi, 0 /*DIB_RGB_COLORS*/);

            if (result == 0)
            {
                _logger.LogWarning("WindowsScreenCapture: GetDIBits returned 0 lines");
                return Array.Empty<byte>();
            }

            return pixels;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WindowsScreenCapture: unexpected error during capture");
            return Array.Empty<byte>();
        }
        finally
        {
            NativeMethods.SelectObject(hMemDC, hOld);
            NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(hMemDC);
            NativeMethods.ReleaseDC(hDesktop, hDC);
        }
    }

    private async void CaptureFrame(object? state)
    {
        if (!_isCapturing) return;
        try
        {
            var screenData = await CaptureFrameAsync();
            FrameCaptured?.Invoke(this, screenData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WindowsScreenCapture: error in capture timer callback");
        }
    }

    public void Dispose() => StopCaptureAsync().Wait();

    // ── P/Invoke declarations ──────────────────────────────────────────────────

    private static class NativeMethods
    {
        public const int  SM_CXSCREEN = 0;
        public const int  SM_CYSCREEN = 1;
        public const uint SRCCOPY     = 0x00CC0020;
        public const uint MONITORINFOF_PRIMARY = 1;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(
            IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc,  int nXSrc,  int nYSrc,
            uint   dwRop);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(
            IntPtr   hdc,
            IntPtr   hBitmap,
            uint     uStartScan,
            uint     cScanLines,
            byte[]?  lpvBits,
            ref BITMAPINFO lpbi,
            uint     uUsage);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint   biSize;
            public int    biWidth;
            public int    biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint   biCompression;
            public uint   biSizeImage;
            public int    biXPelsPerMeter;
            public int    biYPelsPerMeter;
            public uint   biClrUsed;
            public uint   biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public uint[]? bmiColors;
        }

        // Monitor enumeration structures and functions

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFOEX
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    }
}
