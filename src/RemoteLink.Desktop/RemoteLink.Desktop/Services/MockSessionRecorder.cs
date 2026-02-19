using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Mock session recorder that simulates recording without actually encoding video.
/// Used for testing and platforms without FFmpeg support.
/// </summary>
public class MockSessionRecorder : ISessionRecorder
{
    private readonly ILogger<MockSessionRecorder> _logger;
    private readonly object _lock = new();

    private bool _isRecording;
    private bool _isPaused;
    private string? _currentFilePath;
    private DateTime _recordingStartTime;
    private TimeSpan _pausedDuration;
    private DateTime? _pauseStartTime;
    private long _frameCount;
    private long _audioChunkCount;

    public bool IsRecording => _isRecording;
    public bool IsPaused => _isPaused;
    public string? CurrentFilePath => _currentFilePath;
    public TimeSpan RecordedDuration
    {
        get
        {
            lock (_lock)
            {
                if (!_isRecording) return TimeSpan.Zero;
                var elapsed = DateTime.UtcNow - _recordingStartTime;
                if (_isPaused && _pauseStartTime.HasValue)
                    elapsed -= (DateTime.UtcNow - _pauseStartTime.Value);
                return elapsed - _pausedDuration;
            }
        }
    }

    public event EventHandler<string>? RecordingStarted;
    public event EventHandler<string>? RecordingStopped;
    public event EventHandler<string>? RecordingError;

    public MockSessionRecorder(ILogger<MockSessionRecorder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<bool> StartRecordingAsync(
        string filePath,
        int frameRate = 15,
        bool includeAudio = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.FromResult(false);

        lock (_lock)
        {
            if (_isRecording)
                return Task.FromResult(false);

            _isRecording = true;
            _isPaused = false;
            _currentFilePath = filePath;
            _recordingStartTime = DateTime.UtcNow;
            _pausedDuration = TimeSpan.Zero;
            _pauseStartTime = null;
            _frameCount = 0;
            _audioChunkCount = 0;
        }

        _logger.LogInformation("Mock recording started to {FilePath} at {FrameRate} FPS (audio: {Audio})",
            filePath, frameRate, includeAudio);

        RecordingStarted?.Invoke(this, filePath);
        return Task.FromResult(true);
    }

    public Task<bool> StopRecordingAsync()
    {
        lock (_lock)
        {
            if (!_isRecording)
                return Task.FromResult(false);

            var filePath = _currentFilePath;
            _isRecording = false;
            _isPaused = false;
            _currentFilePath = null;

            _logger.LogInformation("Mock recording stopped: {FilePath} ({Frames} frames, {Audio} audio chunks)",
                filePath, _frameCount, _audioChunkCount);

            RecordingStopped?.Invoke(this, filePath ?? "unknown");
            return Task.FromResult(true);
        }
    }

    public Task<bool> PauseRecordingAsync()
    {
        lock (_lock)
        {
            if (!_isRecording || _isPaused)
                return Task.FromResult(false);

            _isPaused = true;
            _pauseStartTime = DateTime.UtcNow;
            _logger.LogInformation("Mock recording paused");
            return Task.FromResult(true);
        }
    }

    public Task<bool> ResumeRecordingAsync()
    {
        lock (_lock)
        {
            if (!_isRecording || !_isPaused)
                return Task.FromResult(false);

            if (_pauseStartTime.HasValue)
            {
                _pausedDuration += DateTime.UtcNow - _pauseStartTime.Value;
                _pauseStartTime = null;
            }

            _isPaused = false;
            _logger.LogInformation("Mock recording resumed");
            return Task.FromResult(true);
        }
    }

    public Task WriteFrameAsync(ScreenData screenData)
    {
        lock (_lock)
        {
            if (_isRecording && !_isPaused)
            {
                _frameCount++;
                _logger.LogTrace("Mock frame written: {Count}", _frameCount);
            }
        }
        return Task.CompletedTask;
    }

    public Task WriteAudioAsync(AudioData audioData)
    {
        lock (_lock)
        {
            if (_isRecording && !_isPaused)
            {
                _audioChunkCount++;
                _logger.LogTrace("Mock audio chunk written: {Count}", _audioChunkCount);
            }
        }
        return Task.CompletedTask;
    }
}
