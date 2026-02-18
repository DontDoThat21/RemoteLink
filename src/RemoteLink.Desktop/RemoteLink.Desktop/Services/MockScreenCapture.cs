using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Mock screen capture for non-Windows platforms (dev/test/Linux CI).
/// Emits random-byte frames so the pipeline can be exercised without real GDI.
/// </summary>
public class MockScreenCapture : IScreenCapture, IDisposable
{
    private Timer? _captureTimer;
    private int _quality = 75;
    private bool _isCapturing;
    private readonly Random _random = new();

    public event EventHandler<ScreenData>? FrameCaptured;

    public Task StartCaptureAsync()
    {
        if (_isCapturing) return Task.CompletedTask;

        _isCapturing = true;
        _captureTimer = new Timer(CaptureFrame, null, 0, 100); // 10 FPS

        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        _isCapturing = false;
        _captureTimer?.Dispose();
        _captureTimer = null;

        return Task.CompletedTask;
    }

    public async Task<ScreenData> CaptureFrameAsync()
    {
        var (width, height) = await GetScreenDimensionsAsync();

        var imageData = new byte[width * height * 3]; // RGB
        _random.NextBytes(imageData);

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
        => Task.FromResult((1920, 1080));

    public void SetQuality(int quality)
        => _quality = Math.Clamp(quality, 1, 100);

    private async void CaptureFrame(object? state)
    {
        if (!_isCapturing) return;
        try
        {
            var screenData = await CaptureFrameAsync();
            FrameCaptured?.Invoke(this, screenData);
        }
        catch (Exception)
        {
            // Suppress in mock
        }
    }

    public void Dispose() => StopCaptureAsync().Wait();
}
