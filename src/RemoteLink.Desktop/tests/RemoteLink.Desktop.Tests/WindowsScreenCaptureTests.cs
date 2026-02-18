using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Tests;

/// <summary>
/// Unit tests for WindowsScreenCapture.
/// The class contains runtime platform guards so all tests are safe to run on Linux CI.
/// On non-Windows the GDI P/Invoke paths are skipped and fallback/empty values are
/// returned — the tests verify correct structural behaviour in both cases.
/// </summary>
public class WindowsScreenCaptureTests
{
    private WindowsScreenCapture CreateCapture() =>
        new WindowsScreenCapture(NullLogger<WindowsScreenCapture>.Instance);

    // ── Interface contract ─────────────────────────────────────────────────────

    [Fact]
    public void Implements_IScreenCapture()
    {
        using var capture = CreateCapture();
        Assert.IsAssignableFrom<IScreenCapture>(capture);
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartCaptureAsync_SetsIsCapturingTrue()
    {
        using var capture = CreateCapture();
        Assert.False(capture.IsCapturing);

        await capture.StartCaptureAsync();

        Assert.True(capture.IsCapturing);
        await capture.StopCaptureAsync();
    }

    [Fact]
    public async Task StopCaptureAsync_SetsIsCapturingFalse()
    {
        using var capture = CreateCapture();
        await capture.StartCaptureAsync();
        Assert.True(capture.IsCapturing);

        await capture.StopCaptureAsync();

        Assert.False(capture.IsCapturing);
    }

    [Fact]
    public async Task StartCaptureAsync_IsIdempotent()
    {
        using var capture = CreateCapture();
        await capture.StartCaptureAsync();
        await capture.StartCaptureAsync(); // second call must not throw

        Assert.True(capture.IsCapturing);
        await capture.StopCaptureAsync();
    }

    [Fact]
    public async Task StopCaptureAsync_WhenNotStarted_DoesNotThrow()
    {
        using var capture = CreateCapture();
        var ex = await Record.ExceptionAsync(() => capture.StopCaptureAsync());
        Assert.Null(ex);
    }

    // ── GetScreenDimensionsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetScreenDimensionsAsync_ReturnsPositiveWidth()
    {
        using var capture = CreateCapture();
        var (width, _) = await capture.GetScreenDimensionsAsync();
        Assert.True(width > 0, $"Expected positive width, got {width}");
    }

    [Fact]
    public async Task GetScreenDimensionsAsync_ReturnsPositiveHeight()
    {
        using var capture = CreateCapture();
        var (_, height) = await capture.GetScreenDimensionsAsync();
        Assert.True(height > 0, $"Expected positive height, got {height}");
    }

    // ── CaptureFrameAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureFrameAsync_ReturnsNonNullScreenData()
    {
        using var capture = CreateCapture();
        var frame = await capture.CaptureFrameAsync();
        Assert.NotNull(frame);
    }

    [Fact]
    public async Task CaptureFrameAsync_WidthMatchesDimensions()
    {
        using var capture = CreateCapture();
        var (expectedWidth, _) = await capture.GetScreenDimensionsAsync();
        var frame = await capture.CaptureFrameAsync();
        Assert.Equal(expectedWidth, frame.Width);
    }

    [Fact]
    public async Task CaptureFrameAsync_HeightMatchesDimensions()
    {
        using var capture = CreateCapture();
        var (_, expectedHeight) = await capture.GetScreenDimensionsAsync();
        var frame = await capture.CaptureFrameAsync();
        Assert.Equal(expectedHeight, frame.Height);
    }

    [Fact]
    public async Task CaptureFrameAsync_FormatIsRaw()
    {
        using var capture = CreateCapture();
        var frame = await capture.CaptureFrameAsync();
        Assert.Equal(ScreenDataFormat.Raw, frame.Format);
    }

    [Fact]
    public async Task CaptureFrameAsync_HasNonNullImageData()
    {
        using var capture = CreateCapture();
        var frame = await capture.CaptureFrameAsync();
        Assert.NotNull(frame.ImageData);
    }

    [Fact]
    public async Task CaptureFrameAsync_OnWindows_HasNonEmptyImageData()
    {
        if (!OperatingSystem.IsWindows())
            return; // Non-Windows returns empty array by design

        using var capture = CreateCapture();
        var frame = await capture.CaptureFrameAsync();
        Assert.NotEmpty(frame.ImageData);
    }

    [Fact]
    public async Task CaptureFrameAsync_OnWindows_ImageDataSizeIsWidthTimesHeightTimes4()
    {
        if (!OperatingSystem.IsWindows())
            return; // BGRA pixel data only available on Windows

        using var capture = CreateCapture();
        var (w, h) = await capture.GetScreenDimensionsAsync();
        var frame = await capture.CaptureFrameAsync();
        Assert.Equal(w * h * 4, frame.ImageData.Length);
    }

    // ── SetQuality ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetQuality_ValidValue_AppliedToNextFrame()
    {
        using var capture = CreateCapture();
        capture.SetQuality(50);
        var frame = await capture.CaptureFrameAsync();
        Assert.Equal(50, frame.Quality);
    }

    [Fact]
    public async Task SetQuality_BelowMinimum_ClampsToOne()
    {
        using var capture = CreateCapture();
        capture.SetQuality(-10);
        var frame = await capture.CaptureFrameAsync();
        Assert.Equal(1, frame.Quality);
    }

    [Fact]
    public async Task SetQuality_AboveMaximum_ClampsToHundred()
    {
        using var capture = CreateCapture();
        capture.SetQuality(200);
        var frame = await capture.CaptureFrameAsync();
        Assert.Equal(100, frame.Quality);
    }

    [Fact]
    public async Task SetQuality_Boundary_One_Accepted()
    {
        using var capture = CreateCapture();
        capture.SetQuality(1);
        var frame = await capture.CaptureFrameAsync();
        Assert.Equal(1, frame.Quality);
    }

    [Fact]
    public async Task SetQuality_Boundary_Hundred_Accepted()
    {
        using var capture = CreateCapture();
        capture.SetQuality(100);
        var frame = await capture.CaptureFrameAsync();
        Assert.Equal(100, frame.Quality);
    }

    // ── FrameCaptured event ────────────────────────────────────────────────────

    [Fact]
    public async Task FrameCaptured_EventFiresAfterStart()
    {
        using var capture = CreateCapture();
        ScreenData? received = null;
        capture.FrameCaptured += (_, data) => received = data;

        await capture.StartCaptureAsync();
        await Task.Delay(350); // wait for at least 3 timer ticks (10 FPS = 100ms each)
        await capture.StopCaptureAsync();

        Assert.NotNull(received);
    }

    [Fact]
    public async Task FrameCaptured_EventNotFiredAfterStop()
    {
        using var capture = CreateCapture();
        int count = 0;
        capture.FrameCaptured += (_, _) => Interlocked.Increment(ref count);

        await capture.StartCaptureAsync();
        await Task.Delay(250);
        await capture.StopCaptureAsync();

        int snapshot = count;
        await Task.Delay(250); // wait and verify count doesn't grow after stop
        Assert.Equal(snapshot, count);
    }

    // ── Dispose ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_WhenCapturing_StopsGracefully()
    {
        var capture = CreateCapture();
        await capture.StartCaptureAsync();
        Assert.True(capture.IsCapturing);

        var ex = Record.Exception(() => capture.Dispose());

        Assert.Null(ex);
    }
}
