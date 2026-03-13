using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Windows.Storage.Streams;

namespace RemoteLink.Desktop.UI;

/// <summary>
/// Windows platform handler for <see cref="GpuFrameView"/>.
/// Uses a Win2D <see cref="CanvasSwapChainPanel"/> as the native view so that each
/// decoded frame is presented directly to the GPU swap chain, completely bypassing the
/// XAML image pipeline and CPU-side compositing that
/// <c>ImageSource.FromStream</c> / <c>WriteableBitmap</c> would require.
/// </summary>
internal sealed class GpuFrameViewHandler
    : ViewHandler<GpuFrameView, CanvasSwapChainPanel>
{
    public static readonly IPropertyMapper<GpuFrameView, GpuFrameViewHandler> GpuMapper =
        new PropertyMapper<GpuFrameView, GpuFrameViewHandler>(ViewMapper);

    public static readonly CommandMapper<GpuFrameView, GpuFrameViewHandler> GpuCommandMapper =
        new CommandMapper<GpuFrameView, GpuFrameViewHandler>(ViewCommandMapper)
        {
            [nameof(GpuFrameView.RenderFrame)] = MapRenderFrame,
        };

    // ── State ──────────────────────────────────────────────────────────────

    private CanvasDevice? _device;
    private CanvasSwapChain? _swapChain;
    private float _panelW;
    private float _panelH;
    private float _dpi = 96f;
    private volatile bool _renderBusy;

    // ── Construction ───────────────────────────────────────────────────────

    public GpuFrameViewHandler() : base(GpuMapper, GpuCommandMapper) { }

    // ── Native view lifecycle ──────────────────────────────────────────────

    protected override CanvasSwapChainPanel CreatePlatformView()
    {
        _device = CanvasDevice.GetSharedDevice();
        var panel = new CanvasSwapChainPanel();
        panel.SizeChanged += OnPanelSizeChanged;
        return panel;
    }

    protected override void DisconnectHandler(CanvasSwapChainPanel platformView)
    {
        platformView.SizeChanged -= OnPanelSizeChanged;

        var sc = _swapChain;
        _swapChain = null;
        sc?.Dispose();
        _device = null;

        base.DisconnectHandler(platformView);
    }

    // ── Size change → swap chain (re)creation ─────────────────────────────

    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var w = (float)Math.Max(1, e.NewSize.Width);
        var h = (float)Math.Max(1, e.NewSize.Height);

        // Only rebuild when the dimensions actually changed.
        if (w == _panelW && h == _panelH)
            return;

        _panelW = w;
        _panelH = h;
        _dpi = (float)(PlatformView.XamlRoot?.RasterizationScale * 96.0 ?? 96.0);

        RecreateSwapChain();
    }

    private void RecreateSwapChain()
    {
        if (_device is null || _panelW <= 0 || _panelH <= 0)
            return;

        // Dispose the old swap chain before replacing it so the GPU resource is freed.
        var old = _swapChain;
        _swapChain = null;
        old?.Dispose();

        var sc = new CanvasSwapChain(_device, _panelW, _panelH, _dpi);
        _swapChain = sc;

        // Attach to the WinUI SwapChainPanel via Win2D's managed property.
        PlatformView.SwapChain = sc;
    }

    // ── Device / swap chain recovery ──────────────────────────────────────

    /// <summary>
    /// Ensures both the <see cref="CanvasDevice"/> and <see cref="CanvasSwapChain"/>
    /// are available.  Called before every frame so the pipeline self-heals after a
    /// device-lost event without waiting for a <c>SizeChanged</c> that may never arrive.
    /// </summary>
    private void EnsureDeviceAndSwapChain()
    {
        if (_device is null)
        {
            try { _device = CanvasDevice.GetSharedDevice(); }
            catch { return; }
        }

        if (_swapChain is null && _panelW > 0 && _panelH > 0)
        {
            _dpi = (float)(PlatformView?.XamlRoot?.RasterizationScale * 96.0 ?? 96.0);
            RecreateSwapChain();
        }
    }

    /// <summary>
    /// Tears down the current device and swap chain, then attempts to reacquire them
    /// so the very next frame can render.
    /// </summary>
    private void RecoverFromDeviceLost()
    {
        var old = _swapChain;
        _swapChain = null;
        old?.Dispose();
        _device = null;

        EnsureDeviceAndSwapChain();
    }

    // ── Frame rendering ────────────────────────────────────────────────────

    private static void MapRenderFrame(
        GpuFrameViewHandler handler, GpuFrameView view, object? args)
    {
        if (args is byte[] bytes)
            _ = handler.RenderFrameAsync(bytes);
    }

    /// <summary>
    /// Decodes <paramref name="imageBytes"/> and presents them to the swap chain.
    /// The <c>_renderBusy</c> gate drops incoming frames while a present is in
    /// progress, so no render tasks queue up.
    /// </summary>
    private async Task RenderFrameAsync(byte[] imageBytes)
    {
        if (_renderBusy)
            return;

        _renderBusy = true;
        try
        {
            EnsureDeviceAndSwapChain();

            var device = _device;
            var swapChain = _swapChain;
            if (device is null || swapChain is null)
                return;

            // ── Decode encoded bytes (JPEG / PNG / BMP) ──────────────────
            // Use an InMemoryRandomAccessStream so WIC can read the format header.
            using var ras = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(imageBytes);
                await writer.StoreAsync();
            }
            ras.Seek(0);

            CanvasBitmap bitmap;
            try
            {
                bitmap = await CanvasBitmap.LoadAsync(device, ras);
            }
            catch (ObjectDisposedException)
            {
                return; // Device or swap chain was torn down mid-render.
            }

            using (bitmap)
            {
                // Re-read after the await — another thread may have disposed
                // the swap chain while the bitmap was decoding.
                swapChain = _swapChain;
                if (swapChain is null)
                    return;

                var sw = (double)swapChain.Size.Width;
                var sh = (double)swapChain.Size.Height;

                // Use pixel dimensions for a precise aspect ratio (DIPs can be
                // fractional on high-DPI displays).
                var px = bitmap.SizeInPixels;
                var bw = (double)px.Width;
                var bh = (double)px.Height;

                // ── Compute aspect-fit (letterbox / pillarbox) rect ───────
                Windows.Foundation.Rect destRect;
                if (bw <= 0 || bh <= 0)
                {
                    destRect = new Windows.Foundation.Rect(0, 0, sw, sh);
                }
                else
                {
                    var bitmapAspect = bw / bh;
                    var surfaceAspect = sw / sh;
                    double renderW, renderH, offsetX, offsetY;

                    if (bitmapAspect > surfaceAspect)
                    {
                        // Wider than surface → fit width, letterbox top/bottom.
                        renderW = sw;
                        renderH = sw / bitmapAspect;
                        offsetX = 0;
                        offsetY = (sh - renderH) / 2;
                    }
                    else
                    {
                        // Taller than surface → fit height, pillarbox left/right.
                        renderH = sh;
                        renderW = sh * bitmapAspect;
                        offsetX = (sw - renderW) / 2;
                        offsetY = 0;
                    }

                    destRect = new Windows.Foundation.Rect(offsetX, offsetY, renderW, renderH);
                }

                // ── Draw and present ──────────────────────────────────────
                // CreateDrawingSession clears to the supplied color (black bars).
                using var ds = swapChain.CreateDrawingSession(Microsoft.UI.Colors.Black);
                ds.DrawImage(bitmap, destRect);
            }

            swapChain.Present();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                       or ObjectDisposedException)
        {
            // Device lost or swap chain invalidated — attempt immediate recovery
            // so the next frame can render without waiting for SizeChanged.
            RecoverFromDeviceLost();
        }
        catch (Exception)
        {
            // Bad image data or transient rendering error — drop this frame.
            // Without this catch-all, the fire-and-forget task would surface as
            // an unobserved exception and hit the WinUI UnhandledException handler.
        }
        finally
        {
            _renderBusy = false;
        }
    }
}
