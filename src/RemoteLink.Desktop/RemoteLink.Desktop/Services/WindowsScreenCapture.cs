using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Windows screen capture using BitBlt / GDI P/Invoke.
/// Captures the primary monitor as 32-bit BGRA pixel data at up to 10 FPS.
///
/// Platform guards are applied at runtime so this class compiles on Linux;
/// it is only instantiated on Windows (see Program.cs DI wiring).
/// On non-Windows the capture methods return empty/fallback data safely.
/// </summary>
public sealed class WindowsScreenCapture : IScreenCapture, IDisposable
{
    private readonly ILogger<WindowsScreenCapture> _logger;
    private Timer? _captureTimer;
    private int _quality = 75;
    private bool _isCapturing;

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

    public Task<(int Width, int Height)> GetScreenDimensionsAsync()
    {
        if (!OperatingSystem.IsWindows())
            return Task.FromResult((1920, 1080)); // safe fallback for non-Windows

        int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        if (w <= 0) w = 1920;
        if (h <= 0) h = 1080;

        return Task.FromResult((w, h));
    }

    public void SetQuality(int quality)
        => _quality = Math.Clamp(quality, 1, 100);

    // ── Internal capture logic ─────────────────────────────────────────────────

    /// <summary>
    /// Uses BitBlt to blit the desktop to an off-screen DC, then GetDIBits to
    /// extract raw 32-bit BGRA pixel data.  Returns an empty array on failure or
    /// when running on a non-Windows platform.
    /// </summary>
    private byte[] CaptureScreenBits(int width, int height)
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<byte>();

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
                hDC,    0, 0,
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
    }
}
