using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using Xunit;

namespace RemoteLink.Desktop.Tests;

/// <summary>
/// Tests for session recording functionality using MockSessionRecorder.
/// (Real SessionRecorder requires FFmpeg and is tested separately)
/// </summary>
public class SessionRecorderTests
{
    private readonly ISessionRecorder _recorder;

    public SessionRecorderTests()
    {
        _recorder = new MockSessionRecorder(NullLogger<MockSessionRecorder>.Instance);
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        Assert.Throws<ArgumentNullException>(() => new MockSessionRecorder(null!));
    }

    [Fact]
    public void IsRecording_IsFalseByDefault()
    {
        Assert.False(_recorder.IsRecording);
        Assert.False(_recorder.IsPaused);
        Assert.Null(_recorder.CurrentFilePath);
        Assert.Equal(TimeSpan.Zero, _recorder.RecordedDuration);
    }

    [Fact]
    public async Task StartRecordingAsync_WithValidPath_StartsRecording()
    {
        var result = await _recorder.StartRecordingAsync("test.mp4");

        Assert.True(result);
        Assert.True(_recorder.IsRecording);
        Assert.Equal("test.mp4", _recorder.CurrentFilePath);
    }

    [Fact]
    public async Task StartRecordingAsync_WithNullPath_ReturnsFalse()
    {
        var result = await _recorder.StartRecordingAsync(null!);

        Assert.False(result);
        Assert.False(_recorder.IsRecording);
    }

    [Fact]
    public async Task StartRecordingAsync_WithEmptyPath_ReturnsFalse()
    {
        var result = await _recorder.StartRecordingAsync("");

        Assert.False(result);
        Assert.False(_recorder.IsRecording);
    }

    [Fact]
    public async Task StartRecordingAsync_WhenAlreadyRecording_ReturnsFalse()
    {
        await _recorder.StartRecordingAsync("first.mp4");
        var result = await _recorder.StartRecordingAsync("second.mp4");

        Assert.False(result);
        Assert.Equal("first.mp4", _recorder.CurrentFilePath);
    }

    [Fact]
    public async Task StopRecordingAsync_WhenRecording_StopsRecording()
    {
        await _recorder.StartRecordingAsync("test.mp4");
        var result = await _recorder.StopRecordingAsync();

        Assert.True(result);
        Assert.False(_recorder.IsRecording);
        Assert.Null(_recorder.CurrentFilePath);
    }

    [Fact]
    public async Task StopRecordingAsync_WhenNotRecording_ReturnsFalse()
    {
        var result = await _recorder.StopRecordingAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task PauseRecordingAsync_WhenRecording_PausesRecording()
    {
        await _recorder.StartRecordingAsync("test.mp4");
        var result = await _recorder.PauseRecordingAsync();

        Assert.True(result);
        Assert.True(_recorder.IsPaused);
        Assert.True(_recorder.IsRecording); // Still recording, just paused
    }

    [Fact]
    public async Task PauseRecordingAsync_WhenNotRecording_ReturnsFalse()
    {
        var result = await _recorder.PauseRecordingAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task PauseRecordingAsync_WhenAlreadyPaused_ReturnsFalse()
    {
        await _recorder.StartRecordingAsync("test.mp4");
        await _recorder.PauseRecordingAsync();
        var result = await _recorder.PauseRecordingAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task ResumeRecordingAsync_WhenPaused_ResumesRecording()
    {
        await _recorder.StartRecordingAsync("test.mp4");
        await _recorder.PauseRecordingAsync();
        var result = await _recorder.ResumeRecordingAsync();

        Assert.True(result);
        Assert.False(_recorder.IsPaused);
        Assert.True(_recorder.IsRecording);
    }

    [Fact]
    public async Task ResumeRecordingAsync_WhenNotPaused_ReturnsFalse()
    {
        await _recorder.StartRecordingAsync("test.mp4");
        var result = await _recorder.ResumeRecordingAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task ResumeRecordingAsync_WhenNotRecording_ReturnsFalse()
    {
        var result = await _recorder.ResumeRecordingAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task WriteFrameAsync_WhenRecording_DoesNotThrow()
    {
        await _recorder.StartRecordingAsync("test.mp4");
        var screenData = new ScreenData
        {
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.Raw,
            Data = new byte[1920 * 1080 * 4]
        };

        await _recorder.WriteFrameAsync(screenData);

        // No exception = success
        Assert.True(_recorder.IsRecording);
    }

    [Fact]
    public async Task WriteFrameAsync_WhenNotRecording_DoesNotThrow()
    {
        var screenData = new ScreenData
        {
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.Raw,
            Data = new byte[1920 * 1080 * 4]
        };

        await _recorder.WriteFrameAsync(screenData);

        // Should be a no-op
        Assert.False(_recorder.IsRecording);
    }

    [Fact]
    public async Task WriteFrameAsync_WhenPaused_DoesNotThrow()
    {
        await _recorder.StartRecordingAsync("test.mp4");
        await _recorder.PauseRecordingAsync();
        var screenData = new ScreenData
        {
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.Raw,
            Data = new byte[1920 * 1080 * 4]
        };

        await _recorder.WriteFrameAsync(screenData);

        // Should be a no-op
        Assert.True(_recorder.IsPaused);
    }

    [Fact]
    public async Task WriteAudioAsync_WhenRecording_DoesNotThrow()
    {
        await _recorder.StartRecordingAsync("test.mp4", includeAudio: true);
        var audioData = new AudioData
        {
            Data = new byte[1024],
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 16,
            DurationMs = 20
        };

        await _recorder.WriteAudioAsync(audioData);

        // No exception = success
        Assert.True(_recorder.IsRecording);
    }

    [Fact]
    public async Task RecordedDuration_IncreasesWhileRecording()
    {
        await _recorder.StartRecordingAsync("test.mp4");
        await Task.Delay(100); // Wait 100ms

        var duration = _recorder.RecordedDuration;
        Assert.True(duration.TotalMilliseconds >= 80); // Allow some margin
    }

    [Fact]
    public async Task RecordedDuration_DoesNotIncludePausedTime()
    {
        await _recorder.StartRecordingAsync("test.mp4");
        await Task.Delay(100);

        await _recorder.PauseRecordingAsync();
        await Task.Delay(100); // Paused for 100ms

        await _recorder.ResumeRecordingAsync();
        await Task.Delay(100);

        // Total elapsed: ~300ms, paused: ~100ms, recorded: ~200ms
        var duration = _recorder.RecordedDuration;
        Assert.True(duration.TotalMilliseconds >= 150 && duration.TotalMilliseconds < 250);
    }

    [Fact]
    public async Task RecordingStarted_EventFires()
    {
        string? firedPath = null;
        _recorder.RecordingStarted += (s, path) => firedPath = path;

        await _recorder.StartRecordingAsync("test.mp4");

        Assert.Equal("test.mp4", firedPath);
    }

    [Fact]
    public async Task RecordingStopped_EventFires()
    {
        string? firedPath = null;
        _recorder.RecordingStopped += (s, path) => firedPath = path;

        await _recorder.StartRecordingAsync("test.mp4");
        await _recorder.StopRecordingAsync();

        Assert.Equal("test.mp4", firedPath);
    }

    [Fact]
    public async Task RecordingError_EventDoesNotFireOnNormalOperation()
    {
        var errorFired = false;
        _recorder.RecordingError += (s, msg) => errorFired = true;

        await _recorder.StartRecordingAsync("test.mp4");
        await _recorder.WriteFrameAsync(new ScreenData
        {
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.Raw,
            Data = new byte[1920 * 1080 * 4]
        });
        await _recorder.StopRecordingAsync();

        Assert.False(errorFired);
    }

    [Fact]
    public async Task CompleteRecordingWorkflow_StartsWritesStops()
    {
        // Start recording
        var startResult = await _recorder.StartRecordingAsync("session-2024.mp4", frameRate: 30);
        Assert.True(startResult);
        Assert.True(_recorder.IsRecording);

        // Write multiple frames
        for (int i = 0; i < 10; i++)
        {
            await _recorder.WriteFrameAsync(new ScreenData
            {
                Width = 1920,
                Height = 1080,
                Format = ScreenDataFormat.Raw,
                Data = new byte[1920 * 1080 * 4],
                Timestamp = DateTime.UtcNow
            });
        }

        // Write audio
        await _recorder.WriteAudioAsync(new AudioData
        {
            Data = new byte[1024],
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 16,
            DurationMs = 20
        });

        // Stop recording
        var stopResult = await _recorder.StopRecordingAsync();
        Assert.True(stopResult);
        Assert.False(_recorder.IsRecording);
        Assert.Null(_recorder.CurrentFilePath);
    }

    [Fact]
    public async Task PauseResumeWorkflow_MaintainsRecordingState()
    {
        await _recorder.StartRecordingAsync("test.mp4");

        // Pause
        var pauseResult = await _recorder.PauseRecordingAsync();
        Assert.True(pauseResult);
        Assert.True(_recorder.IsRecording);
        Assert.True(_recorder.IsPaused);

        // Resume
        var resumeResult = await _recorder.ResumeRecordingAsync();
        Assert.True(resumeResult);
        Assert.True(_recorder.IsRecording);
        Assert.False(_recorder.IsPaused);

        // Stop
        var stopResult = await _recorder.StopRecordingAsync();
        Assert.True(stopResult);
        Assert.False(_recorder.IsRecording);
        Assert.False(_recorder.IsPaused);
    }

    [Fact]
    public async Task MultipleStartStopCycles_Work()
    {
        // First recording
        await _recorder.StartRecordingAsync("first.mp4");
        await _recorder.WriteFrameAsync(CreateTestFrame());
        await _recorder.StopRecordingAsync();

        // Second recording
        await _recorder.StartRecordingAsync("second.mp4");
        await _recorder.WriteFrameAsync(CreateTestFrame());
        await _recorder.StopRecordingAsync();

        Assert.False(_recorder.IsRecording);
        Assert.Null(_recorder.CurrentFilePath);
    }

    private ScreenData CreateTestFrame()
    {
        return new ScreenData
        {
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.Raw,
            Data = new byte[1920 * 1080 * 4],
            Timestamp = DateTime.UtcNow
        };
    }
}
