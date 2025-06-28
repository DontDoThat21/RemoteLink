using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Mock screen capture implementation for cross-platform compatibility
/// In production, this would be replaced with platform-specific implementations
/// </summary>
public class MockScreenCapture : IScreenCapture
{
    private Timer? _captureTimer;
    private int _quality = 75;
    private bool _isCapturing;
    private readonly Random _random = new();

    public event EventHandler<ScreenData>? FrameCaptured;

    public async Task StartCaptureAsync()
    {
        if (_isCapturing) return;

        _isCapturing = true;
        _captureTimer = new Timer(CaptureFrame, null, 0, 100); // 10 FPS
        
        await Task.CompletedTask;
    }

    public async Task StopCaptureAsync()
    {
        _isCapturing = false;
        _captureTimer?.Dispose();
        _captureTimer = null;
        
        await Task.CompletedTask;
    }

    public async Task<ScreenData> CaptureFrameAsync()
    {
        var (width, height) = await GetScreenDimensionsAsync();
        
        // Generate mock image data (simple pattern)
        var imageData = new byte[width * height * 3]; // RGB format
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

    public async Task<(int Width, int Height)> GetScreenDimensionsAsync()
    {
        await Task.CompletedTask;
        return (1920, 1080); // Mock resolution
    }

    public void SetQuality(int quality)
    {
        _quality = Math.Clamp(quality, 1, 100);
    }

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
            // Log error in production
        }
    }

    public void Dispose()
    {
        StopCaptureAsync().Wait();
    }
}