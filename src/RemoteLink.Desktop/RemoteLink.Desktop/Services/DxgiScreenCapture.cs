using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// High-performance Windows screen capture using DXGI Desktop Duplication API.
/// Falls back to GDI BitBlt when Desktop Duplication is unavailable (e.g. no GPU,
/// RDP session, Windows 7, or secure desktop).
///
/// DXGI Desktop Duplication is the same API that OBS, TeamViewer, and AnyDesk use.
/// It captures at GPU speed (~1-2ms per frame vs ~20-30ms for GDI) and natively
/// provides dirty-rectangle information.
/// </summary>
public sealed class DxgiScreenCapture : IScreenCapture, IDisposable
{
    private readonly ILogger<DxgiScreenCapture> _logger;
    private readonly object _monitorLock = new();
    private Timer? _captureTimer;
    private int _quality = 75;
    private volatile bool _isCapturing;
    private string? _selectedMonitorId;
    private List<MonitorInfo> _cachedMonitors = new();

    // DXGI state
    private IntPtr _dxgiOutputDuplication;
    private IntPtr _d3dDevice;
    private IntPtr _d3dContext;
    private IntPtr _stagingTexture;
    private int _dxgiWidth;
    private int _dxgiHeight;
    private bool _dxgiInitialized;
    private bool _dxgiFailed;
    private readonly object _dxgiLock = new();

    // Reusable pixel buffer to avoid per-frame allocation
    private byte[]? _pixelBuffer;

    public bool IsCapturing => _isCapturing;

    public event EventHandler<ScreenData>? FrameCaptured;

    public DxgiScreenCapture(ILogger<DxgiScreenCapture> logger)
    {
        _logger = logger;
    }

    // ── IScreenCapture ─────────────────────────────────────────────────────────

    public Task StartCaptureAsync()
    {
        if (_isCapturing) return Task.CompletedTask;

        _isCapturing = true;
        // 33ms ≈ 30 FPS capture rate (actual send rate controlled by host)
        _captureTimer = new Timer(CaptureFrame, null, 0, 33);
        _logger.LogInformation("DxgiScreenCapture: started (DXGI preferred, GDI fallback)");
        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        _isCapturing = false;
        _captureTimer?.Dispose();
        _captureTimer = null;
        ReleaseDxgi();
        _logger.LogInformation("DxgiScreenCapture: stopped");
        return Task.CompletedTask;
    }

    public async Task<ScreenData> CaptureFrameAsync()
    {
        var (width, height) = await GetScreenDimensionsAsync();

        // Try DXGI first, fall back to GDI
        var imageData = CaptureDxgi(width, height);
        if (imageData == null || imageData.Length == 0)
            imageData = CaptureGdiBitBlt(width, height);

        return new ScreenData
        {
            ImageData = imageData ?? Array.Empty<byte>(),
            Width = width,
            Height = height,
            Format = ScreenDataFormat.Raw,
            Quality = _quality
        };
    }

    public async Task<(int Width, int Height)> GetScreenDimensionsAsync()
    {
        if (!OperatingSystem.IsWindows())
            return (1920, 1080);

        string? selectedId;
        lock (_monitorLock)
        {
            selectedId = _selectedMonitorId;
        }

        if (!string.IsNullOrEmpty(selectedId))
        {
            var monitors = await GetMonitorsAsync();
            var selected = monitors.FirstOrDefault(m => m.Id == selectedId);
            if (selected != null)
                return (selected.Width, selected.Height);
        }

        int w = GdiNativeMethods.GetSystemMetrics(GdiNativeMethods.SM_CXSCREEN);
        int h = GdiNativeMethods.GetSystemMetrics(GdiNativeMethods.SM_CYSCREEN);

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
            GdiNativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumProc, IntPtr.Zero);
            return Task.FromResult<IReadOnlyList<MonitorInfo>>(_cachedMonitors.ToList());
        }
    }

    public Task<bool> SelectMonitorAsync(string monitorId)
    {
        lock (_monitorLock)
        {
            _selectedMonitorId = monitorId;
            // Reset DXGI so it re-initializes for the new output
            ReleaseDxgi();
            _logger.LogInformation("DxgiScreenCapture: selected monitor {MonitorId}", monitorId);
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

    // ── DXGI Desktop Duplication ──────────────────────────────────────────────

    private byte[]? CaptureDxgi(int width, int height)
    {
        if (!OperatingSystem.IsWindows() || _dxgiFailed)
            return null;

        lock (_dxgiLock)
        {
            try
            {
                if (!_dxgiInitialized)
                {
                    if (!InitializeDxgi(width, height))
                    {
                        _dxgiFailed = true;
                        _logger.LogWarning("DxgiScreenCapture: DXGI Desktop Duplication unavailable, using GDI fallback");
                        return null;
                    }
                }

                // Acquire the next frame (timeout 100ms)
                int hr = DxgiNativeMethods.IDXGIOutputDuplication_AcquireNextFrame(
                    _dxgiOutputDuplication, 100, out var frameInfo, out var desktopResource);

                if (hr < 0)
                {
                    // DXGI_ERROR_WAIT_TIMEOUT or DXGI_ERROR_ACCESS_LOST
                    if (hr == unchecked((int)0x887A0027)) // DXGI_ERROR_WAIT_TIMEOUT — no new frame
                        return null;

                    if (hr == unchecked((int)0x887A0026)) // DXGI_ERROR_ACCESS_LOST
                    {
                        _logger.LogInformation("DxgiScreenCapture: access lost, reinitializing");
                        ReleaseDxgiCore();
                        return null;
                    }

                    return null;
                }

                try
                {
                    // QI for ID3D11Texture2D from the desktop resource
                    var iid = typeof(ID3D11Texture2D).GUID;
                    hr = Marshal.QueryInterface(desktopResource, ref iid, out var texPtr);
                    if (hr < 0)
                        return null;

                    try
                    {
                        // Copy to staging texture (GPU→CPU readable)
                        DxgiNativeMethods.ID3D11DeviceContext_CopyResource(_d3dContext, _stagingTexture, texPtr);

                        // Map the staging texture
                        hr = DxgiNativeMethods.ID3D11DeviceContext_Map(
                            _d3dContext, _stagingTexture, 0,
                            4 /* D3D11_MAP_READ */, 0, out var mappedResource);

                        if (hr < 0)
                            return null;

                        try
                        {
                            // Ensure buffer is allocated
                            int bufferSize = width * height * 4;
                            if (_pixelBuffer == null || _pixelBuffer.Length != bufferSize)
                                _pixelBuffer = new byte[bufferSize];

                            // Copy row-by-row (mapped pitch may differ from width*4)
                            int rowPitch = (int)mappedResource.RowPitch;
                            int rowBytes = width * 4;
                            for (int y = 0; y < height; y++)
                            {
                                Marshal.Copy(
                                    mappedResource.pData + y * rowPitch,
                                    _pixelBuffer,
                                    y * rowBytes,
                                    rowBytes);
                            }

                            return _pixelBuffer;
                        }
                        finally
                        {
                            DxgiNativeMethods.ID3D11DeviceContext_Unmap(_d3dContext, _stagingTexture, 0);
                        }
                    }
                    finally
                    {
                        Marshal.Release(texPtr);
                    }
                }
                finally
                {
                    Marshal.Release(desktopResource);
                    DxgiNativeMethods.IDXGIOutputDuplication_ReleaseFrame(_dxgiOutputDuplication);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DxgiScreenCapture: DXGI capture error, will use GDI fallback");
                ReleaseDxgiCore();
                return null;
            }
        }
    }

    private bool InitializeDxgi(int width, int height)
    {
        try
        {
            // Create D3D11 device
            int hr = DxgiNativeMethods.D3D11CreateDevice(
                IntPtr.Zero,       // default adapter
                1,                 // D3D_DRIVER_TYPE_HARDWARE
                IntPtr.Zero,       // no software module
                0,                 // flags
                IntPtr.Zero,       // feature levels (null = default)
                0,                 // num feature levels
                7,                 // SDK version (D3D11_SDK_VERSION)
                out _d3dDevice,
                out _,
                out _d3dContext);

            if (hr < 0 || _d3dDevice == IntPtr.Zero)
            {
                _logger.LogWarning("DxgiScreenCapture: D3D11CreateDevice failed (hr=0x{Hr:X8})", hr);
                return false;
            }

            // Get DXGI device from D3D device
            var dxgiDeviceIid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
            hr = Marshal.QueryInterface(_d3dDevice, ref dxgiDeviceIid, out var dxgiDevice);
            if (hr < 0) return false;

            try
            {
                // Get DXGI adapter
                hr = DxgiNativeMethods.IDXGIDevice_GetAdapter(dxgiDevice, out var dxgiAdapter);
                if (hr < 0) return false;

                try
                {
                    // Get the first output (primary monitor)
                    hr = DxgiNativeMethods.IDXGIAdapter_EnumOutputs(dxgiAdapter, 0, out var dxgiOutput);
                    if (hr < 0) return false;

                    try
                    {
                        // QI for IDXGIOutput1
                        var output1Iid = new Guid("00cddea8-939b-4b83-a340-a685226666cc"); // IDXGIOutput1
                        hr = Marshal.QueryInterface(dxgiOutput, ref output1Iid, out var dxgiOutput1);
                        if (hr < 0) return false;

                        try
                        {
                            // Create desktop duplication
                            hr = DxgiNativeMethods.IDXGIOutput1_DuplicateOutput(
                                dxgiOutput1, _d3dDevice, out _dxgiOutputDuplication);

                            if (hr < 0)
                            {
                                _logger.LogWarning(
                                    "DxgiScreenCapture: DuplicateOutput failed (hr=0x{Hr:X8}). " +
                                    "This can happen in RDP, UAC prompts, or without GPU support.",
                                    hr);
                                return false;
                            }

                            // Create a CPU-readable staging texture
                            var desc = new DxgiNativeMethods.D3D11_TEXTURE2D_DESC
                            {
                                Width = (uint)width,
                                Height = (uint)height,
                                MipLevels = 1,
                                ArraySize = 1,
                                Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                                SampleDescCount = 1,
                                SampleDescQuality = 0,
                                Usage = 3,   // D3D11_USAGE_STAGING
                                BindFlags = 0,
                                CPUAccessFlags = 0x20000, // D3D11_CPU_ACCESS_READ
                                MiscFlags = 0
                            };

                            hr = DxgiNativeMethods.ID3D11Device_CreateTexture2D(
                                _d3dDevice, ref desc, IntPtr.Zero, out _stagingTexture);

                            if (hr < 0)
                            {
                                _logger.LogWarning("DxgiScreenCapture: CreateTexture2D for staging failed (hr=0x{Hr:X8})", hr);
                                return false;
                            }

                            _dxgiWidth = width;
                            _dxgiHeight = height;
                            _dxgiInitialized = true;
                            _logger.LogInformation(
                                "DxgiScreenCapture: DXGI Desktop Duplication initialized ({Width}x{Height})",
                                width, height);
                            return true;
                        }
                        finally
                        {
                            if (!_dxgiInitialized) Marshal.Release(dxgiOutput1);
                        }
                    }
                    finally
                    {
                        Marshal.Release(dxgiOutput);
                    }
                }
                finally
                {
                    Marshal.Release(dxgiAdapter);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DxgiScreenCapture: failed to initialize DXGI Desktop Duplication");
            ReleaseDxgiCore();
            return false;
        }
    }

    private void ReleaseDxgi()
    {
        lock (_dxgiLock)
        {
            ReleaseDxgiCore();
        }
    }

    private void ReleaseDxgiCore()
    {
        if (_stagingTexture != IntPtr.Zero) { Marshal.Release(_stagingTexture); _stagingTexture = IntPtr.Zero; }
        if (_dxgiOutputDuplication != IntPtr.Zero) { Marshal.Release(_dxgiOutputDuplication); _dxgiOutputDuplication = IntPtr.Zero; }
        if (_d3dContext != IntPtr.Zero) { Marshal.Release(_d3dContext); _d3dContext = IntPtr.Zero; }
        if (_d3dDevice != IntPtr.Zero) { Marshal.Release(_d3dDevice); _d3dDevice = IntPtr.Zero; }
        _dxgiInitialized = false;
        _dxgiFailed = false;
    }

    // ── GDI fallback ─────────────────────────────────────────────────────────

    private byte[] CaptureGdiBitBlt(int width, int height)
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<byte>();

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

        IntPtr hDesktop = GdiNativeMethods.GetDesktopWindow();
        IntPtr hDC = GdiNativeMethods.GetDC(hDesktop);

        if (hDC == IntPtr.Zero)
            return Array.Empty<byte>();

        IntPtr hMemDC = GdiNativeMethods.CreateCompatibleDC(hDC);
        IntPtr hBitmap = GdiNativeMethods.CreateCompatibleBitmap(hDC, width, height);
        IntPtr hOld = GdiNativeMethods.SelectObject(hMemDC, hBitmap);

        try
        {
            bool blitOk = GdiNativeMethods.BitBlt(
                hMemDC, 0, 0, width, height,
                hDC, srcX, srcY,
                GdiNativeMethods.SRCCOPY);

            if (!blitOk)
                return Array.Empty<byte>();

            var bmi = new GdiNativeMethods.BITMAPINFO
            {
                bmiHeader = new GdiNativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<GdiNativeMethods.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                    biSizeImage = 0
                }
            };

            byte[] pixels = new byte[width * 4 * height];
            int result = GdiNativeMethods.GetDIBits(
                hMemDC, hBitmap, 0, (uint)height, pixels, ref bmi, 0);

            return result == 0 ? Array.Empty<byte>() : pixels;
        }
        finally
        {
            GdiNativeMethods.SelectObject(hMemDC, hOld);
            GdiNativeMethods.DeleteObject(hBitmap);
            GdiNativeMethods.DeleteDC(hMemDC);
            GdiNativeMethods.ReleaseDC(hDesktop, hDC);
        }
    }

    // ── Timer callback ───────────────────────────────────────────────────────

    private async void CaptureFrame(object? state)
    {
        if (!_isCapturing) return;
        try
        {
            var screenData = await CaptureFrameAsync();
            if (screenData.ImageData.Length > 0)
                FrameCaptured?.Invoke(this, screenData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DxgiScreenCapture: error in capture timer callback");
        }
    }

    // ── Monitor enumeration callback ─────────────────────────────────────────

    private bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref GdiNativeMethods.RECT lprcMonitor, IntPtr dwData)
    {
        if (!OperatingSystem.IsWindows())
            return true;

        var info = new GdiNativeMethods.MONITORINFOEX();
        info.cbSize = (uint)Marshal.SizeOf<GdiNativeMethods.MONITORINFOEX>();

        if (GdiNativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            var monitorInfo = new MonitorInfo
            {
                Id = hMonitor.ToString(),
                Name = info.szDevice,
                IsPrimary = (info.dwFlags & GdiNativeMethods.MONITORINFOF_PRIMARY) != 0,
                Width = info.rcMonitor.Right - info.rcMonitor.Left,
                Height = info.rcMonitor.Bottom - info.rcMonitor.Top,
                Left = info.rcMonitor.Left,
                Top = info.rcMonitor.Top
            };

            _cachedMonitors.Add(monitorInfo);
        }

        return true;
    }

    public void Dispose()
    {
        StopCaptureAsync().Wait();
        ReleaseDxgi();
    }

    // ── DXGI COM interop ─────────────────────────────────────────────────────

    [ComImport, Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3D11Texture2D { }

    private static class DxgiNativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_OUTDUPL_FRAME_INFO
        {
            public long LastPresentTime;
            public long LastMouseUpdateTime;
            public uint AccumulatedFrames;
            public int RectsCoalesced;
            public int ProtectedContentMaskedOut;
            public DXGI_OUTDUPL_POINTER_POSITION PointerPosition;
            public uint TotalMetadataBufferSize;
            public uint PointerShapeBufferSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_OUTDUPL_POINTER_POSITION
        {
            public int X;
            public int Y;
            public int Visible;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11_MAPPED_SUBRESOURCE
        {
            public IntPtr pData;
            public uint RowPitch;
            public uint DepthPitch;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11_TEXTURE2D_DESC
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public uint Format;
            public uint SampleDescCount;
            public uint SampleDescQuality;
            public uint Usage;
            public uint BindFlags;
            public uint CPUAccessFlags;
            public uint MiscFlags;
        }

        [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            int DriverType,
            IntPtr Software,
            uint Flags,
            IntPtr pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,
            out IntPtr ppDevice,
            out int pFeatureLevel,
            out IntPtr ppImmediateContext);

        // IDXGIDevice::GetAdapter (vtable slot 7: IUnknown=3 + IDXGIObject=4 + IDXGIDevice=7)
        public static int IDXGIDevice_GetAdapter(IntPtr dxgiDevice, out IntPtr adapter)
        {
            // IDXGIDevice::GetAdapter is at vtable index 7
            var vtable = Marshal.ReadIntPtr(dxgiDevice);
            var fn = Marshal.GetDelegateForFunctionPointer<IDXGIDevice_GetAdapterDelegate>(
                Marshal.ReadIntPtr(vtable, 7 * IntPtr.Size));
            return fn(dxgiDevice, out adapter);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIDevice_GetAdapterDelegate(IntPtr self, out IntPtr adapter);

        // IDXGIAdapter::EnumOutputs (vtable slot 7: IUnknown=3 + IDXGIObject=4 + IDXGIAdapter=7)
        public static int IDXGIAdapter_EnumOutputs(IntPtr adapter, uint index, out IntPtr output)
        {
            var vtable = Marshal.ReadIntPtr(adapter);
            var fn = Marshal.GetDelegateForFunctionPointer<IDXGIAdapter_EnumOutputsDelegate>(
                Marshal.ReadIntPtr(vtable, 7 * IntPtr.Size));
            return fn(adapter, index, out output);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIAdapter_EnumOutputsDelegate(IntPtr self, uint index, out IntPtr output);

        // IDXGIOutput1::DuplicateOutput (vtable slot 22)
        // IDXGIOutput1 inherits: IUnknown(3) + IDXGIObject(4) + IDXGIOutput(18) + IDXGIOutput1(22)
        public static int IDXGIOutput1_DuplicateOutput(IntPtr output1, IntPtr device, out IntPtr duplication)
        {
            var vtable = Marshal.ReadIntPtr(output1);
            var fn = Marshal.GetDelegateForFunctionPointer<IDXGIOutput1_DuplicateOutputDelegate>(
                Marshal.ReadIntPtr(vtable, 22 * IntPtr.Size));
            return fn(output1, device, out duplication);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIOutput1_DuplicateOutputDelegate(IntPtr self, IntPtr device, out IntPtr duplication);

        // IDXGIOutputDuplication::AcquireNextFrame (vtable slot 7)
        // IUnknown(3) + IDXGIObject(4) + IDXGIOutputDuplication(7)
        public static int IDXGIOutputDuplication_AcquireNextFrame(
            IntPtr duplication, uint timeoutMs, out DXGI_OUTDUPL_FRAME_INFO frameInfo, out IntPtr desktopResource)
        {
            var vtable = Marshal.ReadIntPtr(duplication);
            var fn = Marshal.GetDelegateForFunctionPointer<IDXGIOutputDuplication_AcquireNextFrameDelegate>(
                Marshal.ReadIntPtr(vtable, 7 * IntPtr.Size));
            return fn(duplication, timeoutMs, out frameInfo, out desktopResource);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIOutputDuplication_AcquireNextFrameDelegate(
            IntPtr self, uint timeoutMs, out DXGI_OUTDUPL_FRAME_INFO frameInfo, out IntPtr desktopResource);

        // IDXGIOutputDuplication::ReleaseFrame (vtable slot 14)
        public static int IDXGIOutputDuplication_ReleaseFrame(IntPtr duplication)
        {
            var vtable = Marshal.ReadIntPtr(duplication);
            var fn = Marshal.GetDelegateForFunctionPointer<IDXGIOutputDuplication_ReleaseFrameDelegate>(
                Marshal.ReadIntPtr(vtable, 14 * IntPtr.Size));
            return fn(duplication);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIOutputDuplication_ReleaseFrameDelegate(IntPtr self);

        // ID3D11DeviceContext::CopyResource (vtable slot 47)
        public static void ID3D11DeviceContext_CopyResource(IntPtr context, IntPtr dst, IntPtr src)
        {
            var vtable = Marshal.ReadIntPtr(context);
            var fn = Marshal.GetDelegateForFunctionPointer<ID3D11DeviceContext_CopyResourceDelegate>(
                Marshal.ReadIntPtr(vtable, 47 * IntPtr.Size));
            fn(context, dst, src);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void ID3D11DeviceContext_CopyResourceDelegate(IntPtr self, IntPtr dst, IntPtr src);

        // ID3D11DeviceContext::Map (vtable slot 14)
        public static int ID3D11DeviceContext_Map(
            IntPtr context, IntPtr resource, uint subresource, uint mapType, uint mapFlags,
            out D3D11_MAPPED_SUBRESOURCE mappedResource)
        {
            var vtable = Marshal.ReadIntPtr(context);
            var fn = Marshal.GetDelegateForFunctionPointer<ID3D11DeviceContext_MapDelegate>(
                Marshal.ReadIntPtr(vtable, 14 * IntPtr.Size));
            return fn(context, resource, subresource, mapType, mapFlags, out mappedResource);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ID3D11DeviceContext_MapDelegate(
            IntPtr self, IntPtr resource, uint subresource, uint mapType, uint mapFlags,
            out D3D11_MAPPED_SUBRESOURCE mappedResource);

        // ID3D11DeviceContext::Unmap (vtable slot 15)
        public static void ID3D11DeviceContext_Unmap(IntPtr context, IntPtr resource, uint subresource)
        {
            var vtable = Marshal.ReadIntPtr(context);
            var fn = Marshal.GetDelegateForFunctionPointer<ID3D11DeviceContext_UnmapDelegate>(
                Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size));
            fn(context, resource, subresource);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void ID3D11DeviceContext_UnmapDelegate(IntPtr self, IntPtr resource, uint subresource);

        // ID3D11Device::CreateTexture2D (vtable slot 5)
        public static int ID3D11Device_CreateTexture2D(
            IntPtr device, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture)
        {
            var vtable = Marshal.ReadIntPtr(device);
            var fn = Marshal.GetDelegateForFunctionPointer<ID3D11Device_CreateTexture2DDelegate>(
                Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size));
            return fn(device, ref desc, initialData, out texture);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ID3D11Device_CreateTexture2DDelegate(
            IntPtr self, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture);
    }

    // ── GDI P/Invoke (fallback) ──────────────────────────────────────────────

    private static class GdiNativeMethods
    {
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;
        public const uint SRCCOPY = 0x00CC0020;
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
            IntPtr hdcSrc, int nXSrc, int nYSrc,
            uint dwRop);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(
            IntPtr hdc, IntPtr hBitmap, uint uStartScan, uint cScanLines,
            byte[]? lpvBits, ref BITMAPINFO lpbi, uint uUsage);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public uint[]? bmiColors;
        }

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
