using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Maui.Handlers;
using Windows.Storage.Streams;

namespace RemoteLink.Desktop.UI;

/// <summary>
/// Windows platform handler for <see cref="GpuFrameView"/>.
/// Uses a Win2D <see cref="CanvasControl"/> as the native view so that each decoded
/// frame is rendered via Direct2D through a <c>SurfaceImageSource</c>, which integrates
/// with the WinUI XAML compositor.  Unlike <c>CanvasSwapChainPanel</c>, this approach
/// works correctly inside WinUI <c>Frame</c> navigation (which MAUI's
/// <c>NavigationPage</c> uses internally) and avoids the <c>COMException</c> /
/// <c>Frame.NavigationFailed</c> that <c>SwapChainPanel</c>-derived controls can
/// trigger during page transitions.
/// </summary>
internal sealed class GpuFrameViewHandler
    : ViewHandler<GpuFrameView, CanvasControl>
{
    public static readonly IPropertyMapper<GpuFrameView, GpuFrameViewHandler> GpuMapper =
        new PropertyMapper<GpuFrameView, GpuFrameViewHandler>(ViewMapper);

    public static readonly CommandMapper<GpuFrameView, GpuFrameViewHandler> GpuCommandMapper =
        new CommandMapper<GpuFrameView, GpuFrameViewHandler>(ViewCommandMapper)
        {
            [nameof(GpuFrameView.RenderFrame)] = MapRenderFrame,
        };

    // ── State ──────────────────────────────────────────────────────────────

    private CanvasBitmap? _currentFrame;
    private volatile bool _renderBusy;

    // ── Construction ───────────────────────────────────────────────────────

    public GpuFrameViewHandler() : base(GpuMapper, GpuCommandMapper) { }

    // ── Native view lifecycle ──────────────────────────────────────────────

    protected override CanvasControl CreatePlatformView()
    {
        var control = new CanvasControl();
        control.Draw += OnDraw;
        return control;
    }

    protected override void DisconnectHandler(CanvasControl platformView)
    {
        platformView.Draw -= OnDraw;

        var frame = _currentFrame;
        _currentFrame = null;
        frame?.Dispose();

        base.DisconnectHandler(platformView);
    }

    // ── Draw handler ──────────────────────────────────────────────────────

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        args.DrawingSession.Clear(Microsoft.UI.Colors.Black);

        var bitmap = _currentFrame;
        if (bitmap is null)
            return;

        var sw = sender.ActualWidth;
        var sh = sender.ActualHeight;

        if (sw <= 0 || sh <= 0)
            return;

        // Use pixel dimensions for a precise aspect ratio (DIPs can be
        // fractional on high-DPI displays).
        var px = bitmap.SizeInPixels;
        var bw = (double)px.Width;
        var bh = (double)px.Height;

        // ── Compute aspect-fit (letterbox / pillarbox) rect ───────────
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

        args.DrawingSession.DrawImage(bitmap, destRect);
    }

    // ── Frame rendering ────────────────────────────────────────────────────

    private static void MapRenderFrame(
        GpuFrameViewHandler handler, GpuFrameView view, object? args)
    {
        if (args is byte[] bytes)
            _ = handler.RenderFrameAsync(bytes);
    }

    /// <summary>
    /// Decodes <paramref name="imageBytes"/> and stores the result as the current frame,
    /// then invalidates the <see cref="CanvasControl"/> so its <c>Draw</c> event fires.
    /// The <c>_renderBusy</c> gate drops incoming frames while a decode is in progress,
    /// so no render tasks queue up.
    /// </summary>
    private async Task RenderFrameAsync(byte[] imageBytes)
    {
        if (_renderBusy)
            return;

        _renderBusy = true;
        try
        {
            var device = CanvasDevice.GetSharedDevice();

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
                return; // Device or control was torn down mid-render.
            }

            var oldFrame = _currentFrame;
            _currentFrame = bitmap;
            oldFrame?.Dispose();

            // Trigger a redraw — the Draw handler will paint the new frame.
            PlatformView?.Invalidate();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                       or ObjectDisposedException)
        {
            // Device lost — next frame will retry with a fresh shared device.
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
