using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Mock audio capture service for testing and non-Windows platforms
/// Generates silent audio or simple test tones
/// </summary>
public class MockAudioCaptureService : IAudioCaptureService
{
    private readonly ILogger<MockAudioCaptureService> _logger;
    private AudioCaptureSettings _settings = new();
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private readonly object _lock = new();

    public bool IsCapturing { get; private set; }
    public AudioCaptureSettings Settings => _settings;

    public event EventHandler<AudioData>? AudioCaptured;

    public MockAudioCaptureService(ILogger<MockAudioCaptureService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (IsCapturing)
            {
                _logger.LogWarning("Mock audio capture already started");
                return Task.CompletedTask;
            }

            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _captureTask = Task.Run(() => GenerateAudioLoop(_captureCts.Token), _captureCts.Token);
            IsCapturing = true;
            _logger.LogInformation("Mock audio capture started (Sample Rate: {Rate}Hz, Channels: {Channels})",
                _settings.SampleRate, _settings.Channels);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? taskToWait;
        lock (_lock)
        {
            if (!IsCapturing)
            {
                return;
            }

            _captureCts?.Cancel();
            taskToWait = _captureTask;
            IsCapturing = false;
        }

        if (taskToWait != null)
        {
            try
            {
                await taskToWait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
        }

        _captureCts?.Dispose();
        _captureCts = null;
        _captureTask = null;
        _logger.LogInformation("Mock audio capture stopped");
    }

    public void UpdateSettings(AudioCaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_lock)
        {
            _settings = settings;
            _logger.LogDebug("Mock audio settings updated: {Rate}Hz, {Channels}ch",
                settings.SampleRate, settings.Channels);
        }
    }

    private async Task GenerateAudioLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_settings.ChunkDurationMs, cancellationToken).ConfigureAwait(false);

                // Calculate buffer size for this chunk
                int samplesPerChunk = (_settings.SampleRate * _settings.ChunkDurationMs) / 1000;
                int bytesPerSample = _settings.BitsPerSample / 8;
                int bufferSize = samplesPerChunk * _settings.Channels * bytesPerSample;

                // Generate silent audio (all zeros)
                byte[] audioData = new byte[bufferSize];

                // Fire event with mock audio data
                if (AudioCaptured != null)
                {
                    var data = new AudioData
                    {
                        Data = audioData,
                        SampleRate = _settings.SampleRate,
                        Channels = _settings.Channels,
                        BitsPerSample = _settings.BitsPerSample,
                        Timestamp = DateTime.UtcNow,
                        DurationMs = _settings.ChunkDurationMs,
                        Format = "PCM"
                    };

                    AudioCaptured.Invoke(this, data);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Mock audio capture cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in mock audio capture loop");
        }
        finally
        {
            // Ensure IsCapturing is set to false when loop exits
            lock (_lock)
            {
                IsCapturing = false;
            }
        }
    }
}
