using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Records remote desktop sessions to MP4 video files using FFmpeg.
/// Spawns an FFmpeg process and pipes raw frames + audio to stdin.
/// </summary>
/// <remarks>
/// Requires FFmpeg to be installed and available in PATH.
/// On Windows: Download from https://ffmpeg.org/download.html
/// On Linux: sudo apt install ffmpeg
/// </remarks>
public class SessionRecorder : ISessionRecorder, IDisposable
{
    private readonly ILogger<SessionRecorder> _logger;
    private readonly object _lock = new();

    private Process? _ffmpegProcess;
    private Stream? _ffmpegInput;
    private bool _isRecording;
    private bool _isPaused;
    private string? _currentFilePath;
    private DateTime _recordingStartTime;
    private TimeSpan _pausedDuration;
    private DateTime? _pauseStartTime;
    private int _frameRate;
    private bool _includeAudio;
    private int _frameWidth;
    private int _frameHeight;
    private long _frameCount;

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

    public SessionRecorder(ILogger<SessionRecorder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> StartRecordingAsync(
        string filePath,
        int frameRate = 15,
        bool includeAudio = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("StartRecording failed: filePath is null or empty");
            return false;
        }

        lock (_lock)
        {
            if (_isRecording)
            {
                _logger.LogWarning("StartRecording failed: already recording to {Path}", _currentFilePath);
                return false;
            }

            _currentFilePath = filePath;
            _frameRate = frameRate;
            _includeAudio = includeAudio;
            _frameWidth = 0;
            _frameHeight = 0;
            _frameCount = 0;
            _recordingStartTime = DateTime.UtcNow;
            _pausedDuration = TimeSpan.Zero;
            _pauseStartTime = null;
            _isPaused = false;
        }

        try
        {
            // Ensure output directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Note: FFmpeg process will be started when the first frame is written
            // (since we need frame dimensions to configure the encoder)
            lock (_lock)
            {
                _isRecording = true;
            }

            _logger.LogInformation("Recording started to {FilePath} at {FrameRate} FPS (audio: {Audio})",
                filePath, frameRate, includeAudio);

            RecordingStarted?.Invoke(this, filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording to {FilePath}", filePath);
            RecordingError?.Invoke(this, $"Failed to start recording: {ex.Message}");
            lock (_lock)
            {
                _isRecording = false;
                _currentFilePath = null;
            }
            return false;
        }
    }

    public async Task<bool> StopRecordingAsync()
    {
        Process? processToStop = null;
        Stream? streamToClose = null;
        string? filePathToReport = null;

        lock (_lock)
        {
            if (!_isRecording)
            {
                _logger.LogWarning("StopRecording called but no recording in progress");
                return false;
            }

            processToStop = _ffmpegProcess;
            streamToClose = _ffmpegInput;
            filePathToReport = _currentFilePath;

            _isRecording = false;
            _isPaused = false;
            _currentFilePath = null;
            _ffmpegProcess = null;
            _ffmpegInput = null;
        }

        try
        {
            // Close stdin to signal FFmpeg to finalize the video
            if (streamToClose != null)
            {
                await streamToClose.FlushAsync();
                streamToClose.Close();
            }

            // Wait for FFmpeg to finish encoding
            if (processToStop != null)
            {
                await Task.Run(() =>
                {
                    if (!processToStop.WaitForExit(5000))
                    {
                        _logger.LogWarning("FFmpeg did not exit within 5 seconds, killing process");
                        processToStop.Kill();
                    }
                });
                processToStop.Dispose();
            }

            _logger.LogInformation("Recording stopped: {FilePath} ({Frames} frames)",
                filePathToReport, _frameCount);

            RecordingStopped?.Invoke(this, filePathToReport ?? "unknown");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping recording");
            RecordingError?.Invoke(this, $"Error stopping recording: {ex.Message}");
            return false;
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
            _logger.LogInformation("Recording paused");
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
            _logger.LogInformation("Recording resumed");
            return Task.FromResult(true);
        }
    }

    public async Task WriteFrameAsync(ScreenData screenData)
    {
        if (screenData?.ImageData == null || screenData.ImageData.Length == 0)
            return;

        lock (_lock)
        {
            if (!_isRecording || _isPaused)
                return;

            // Initialize FFmpeg on first frame (now we know dimensions)
            if (_ffmpegProcess == null)
            {
                _frameWidth = screenData.Width;
                _frameHeight = screenData.Height;
                StartFFmpegProcess();
            }
        }

        try
        {
            // FFmpeg expects raw RGBA frames (or convert to RGB24)
            // For simplicity, we'll use the raw BGRA data and let FFmpeg handle conversion
            var frameData = ConvertFrameForFFmpeg(screenData);
            if (frameData != null && _ffmpegInput != null)
            {
                await _ffmpegInput.WriteAsync(frameData, 0, frameData.Length);
                Interlocked.Increment(ref _frameCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing frame to recording");
            RecordingError?.Invoke(this, $"Error writing frame: {ex.Message}");
        }
    }

    public Task WriteAudioAsync(AudioData audioData)
    {
        // TODO: Audio recording support
        // This would require a separate FFmpeg audio input pipe or muxing
        // For now, we only support video recording
        return Task.CompletedTask;
    }

    private void StartFFmpegProcess()
    {
        if (_frameWidth <= 0 || _frameHeight <= 0)
        {
            _logger.LogError("Cannot start FFmpeg: invalid frame dimensions {W}x{H}", _frameWidth, _frameHeight);
            return;
        }

        try
        {
            var args = BuildFFmpegArguments();
            _logger.LogDebug("Starting FFmpeg: ffmpeg {Args}", args);

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _ffmpegProcess = Process.Start(psi);
            if (_ffmpegProcess == null)
            {
                _logger.LogError("Failed to start FFmpeg process");
                RecordingError?.Invoke(this, "Failed to start FFmpeg");
                return;
            }

            _ffmpegInput = _ffmpegProcess.StandardInput.BaseStream;

            // Log FFmpeg output asynchronously
            _ = Task.Run(() => LogFFmpegOutput(_ffmpegProcess.StandardError));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start FFmpeg process");
            RecordingError?.Invoke(this, $"FFmpeg start failed: {ex.Message}");
        }
    }

    private string BuildFFmpegArguments()
    {
        // Input: raw video from stdin
        // -f rawvideo: input format is raw frames
        // -pix_fmt bgra: pixel format (Windows screen capture uses BGRA)
        // -s {W}x{H}: frame size
        // -r {fps}: frame rate
        // -i -: read from stdin
        //
        // Output:
        // -c:v libx264: H.264 codec
        // -preset ultrafast: encoding speed (ultrafast for real-time)
        // -pix_fmt yuv420p: output pixel format (widely compatible)
        // -y: overwrite output file
        var sb = new StringBuilder();
        sb.Append($"-f rawvideo -pix_fmt bgra -s {_frameWidth}x{_frameHeight} -r {_frameRate} -i - ");
        sb.Append("-c:v libx264 -preset ultrafast -pix_fmt yuv420p ");
        sb.Append($"-y \"{_currentFilePath}\"");
        return sb.ToString();
    }

    private byte[]? ConvertFrameForFFmpeg(ScreenData screenData)
    {
        // ScreenData.ImageData is already in BGRA format from WindowsScreenCapture
        // FFmpeg will accept it with -pix_fmt bgra
        if (screenData.Format == ScreenDataFormat.Raw)
            return screenData.ImageData;

        // If JPEG/PNG, we'd need to decode first (not implemented here)
        _logger.LogWarning("Unsupported screen format for recording: {Format}", screenData.Format);
        return null;
    }

    private async Task LogFFmpegOutput(StreamReader stderr)
    {
        try
        {
            while (!stderr.EndOfStream)
            {
                var line = await stderr.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                    _logger.LogDebug("FFmpeg: {Output}", line);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading FFmpeg output");
        }
    }

    public void Dispose()
    {
        if (_isRecording)
            _ = StopRecordingAsync().GetAwaiter().GetResult();

        _ffmpegInput?.Dispose();
        _ffmpegProcess?.Dispose();
    }
}
