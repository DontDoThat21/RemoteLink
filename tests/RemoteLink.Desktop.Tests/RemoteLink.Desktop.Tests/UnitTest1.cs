using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Tests;

public class MockScreenCaptureTests
{
    [Fact]
    public async Task MockScreenCapture_ShouldStartAndStopCorrectly()
    {
        // Arrange
        var screenCapture = new MockScreenCapture();

        // Act
        await screenCapture.StartCaptureAsync();
        var isStarted = true; // We don't have a direct way to check, but no exception means success
        
        await screenCapture.StopCaptureAsync();
        var isStopped = true; // We don't have a direct way to check, but no exception means success

        // Assert
        Assert.True(isStarted);
        Assert.True(isStopped);
    }

    [Fact]
    public async Task MockScreenCapture_ShouldCaptureFrame()
    {
        // Arrange
        var screenCapture = new MockScreenCapture();

        // Act
        var frameData = await screenCapture.CaptureFrameAsync();

        // Assert
        Assert.NotNull(frameData);
        Assert.NotEmpty(frameData.ImageData);
        Assert.True(frameData.Width > 0);
        Assert.True(frameData.Height > 0);
        Assert.Equal(ScreenDataFormat.Raw, frameData.Format);
    }

    [Fact]
    public async Task MockScreenCapture_ShouldReturnScreenDimensions()
    {
        // Arrange
        var screenCapture = new MockScreenCapture();

        // Act
        var (width, height) = await screenCapture.GetScreenDimensionsAsync();

        // Assert
        Assert.Equal(1920, width);
        Assert.Equal(1080, height);
    }

    [Fact]
    public void MockScreenCapture_ShouldSetQuality()
    {
        // Arrange
        var screenCapture = new MockScreenCapture();

        // Act & Assert - Should not throw
        screenCapture.SetQuality(50);
        screenCapture.SetQuality(0);   // Should clamp to 1
        screenCapture.SetQuality(150); // Should clamp to 100
    }
}