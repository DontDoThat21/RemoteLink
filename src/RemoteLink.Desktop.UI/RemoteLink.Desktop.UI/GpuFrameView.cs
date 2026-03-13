namespace RemoteLink.Desktop.UI;

/// <summary>
/// A MAUI <see cref="View"/> that renders remote desktop frames directly to a GPU swap
/// chain (Win2D <c>CanvasSwapChain</c> on Windows).  Frames bypass the XAML image
/// pipeline — there is no <c>ImageSource.FromStream</c> CPU decode, no
/// <c>WriteableBitmap</c> copy, and no XAML compositor texture upload on the UI thread.
/// The platform handler decodes the encoded bytes asynchronously and presents to the
/// swap chain from a background task, keeping the UI thread free.
/// </summary>
public class GpuFrameView : View
{
    /// <summary>
    /// Queues <paramref name="imageBytes"/> (JPEG, PNG, or BMP) for rendering on the GPU
    /// surface.  Safe to call from any thread; the actual decode + present is dispatched
    /// to a background task inside the platform handler.
    /// </summary>
    public void RenderFrame(byte[] imageBytes) =>
        Handler?.Invoke(nameof(RenderFrame), imageBytes);
}
