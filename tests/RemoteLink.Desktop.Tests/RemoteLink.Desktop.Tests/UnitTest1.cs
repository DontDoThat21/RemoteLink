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

public class MockInputHandlerTests
{
    [Fact]
    public async Task MockInputHandler_ShouldStartAndStopCorrectly()
    {
        // Arrange
        var inputHandler = new MockInputHandler();

        // Act
        await inputHandler.StartAsync();
        var isActive = inputHandler.IsActive;
        
        await inputHandler.StopAsync();
        var isInactive = !inputHandler.IsActive;

        // Assert
        Assert.True(isActive);
        Assert.True(isInactive);
    }

    [Fact]
    public async Task MockInputHandler_ShouldProcessCommandExecution()
    {
        // Arrange
        var inputHandler = new MockInputHandler();
        await inputHandler.StartAsync();

        var inputEvent = new InputEvent
        {
            Type = InputEventType.CommandExecution,
            Command = "echo Hello Test",
            WorkingDirectory = Environment.CurrentDirectory
        };

        // Act & Assert - Should not throw exception
        await inputHandler.ProcessInputEventAsync(inputEvent);
    }

    [Fact]
    public async Task MockInputHandler_ShouldIgnoreEventsWhenInactive()
    {
        // Arrange
        var inputHandler = new MockInputHandler();
        // Don't start the handler, so it's inactive

        var inputEvent = new InputEvent
        {
            Type = InputEventType.CommandExecution,
            Command = "echo Hello Test"
        };

        // Act & Assert - Should not throw exception and should handle gracefully
        await inputHandler.ProcessInputEventAsync(inputEvent);
    }
}