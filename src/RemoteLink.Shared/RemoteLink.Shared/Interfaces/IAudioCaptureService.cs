using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Interface for capturing system audio
/// </summary>
public interface IAudioCaptureService
{
    /// <summary>
    /// Start capturing audio
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop capturing audio
    /// </summary>
    /// <returns>Task representing the async operation</returns>
    Task StopAsync();

    /// <summary>
    /// Whether audio capture is currently active
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Get audio capture settings
    /// </summary>
    AudioCaptureSettings Settings { get; }

    /// <summary>
    /// Update audio capture settings
    /// </summary>
    void UpdateSettings(AudioCaptureSettings settings);

    /// <summary>
    /// Event fired when audio data is captured
    /// </summary>
    event EventHandler<AudioData> AudioCaptured;
}

/// <summary>
/// Settings for audio capture
/// </summary>
public class AudioCaptureSettings
{
    /// <summary>
    /// Sample rate in Hz (default: 48000)
    /// </summary>
    public int SampleRate { get; set; } = 48000;

    /// <summary>
    /// Number of channels (1 = mono, 2 = stereo, default: 2)
    /// </summary>
    public int Channels { get; set; } = 2;

    /// <summary>
    /// Bits per sample (default: 16)
    /// </summary>
    public int BitsPerSample { get; set; } = 16;

    /// <summary>
    /// Chunk duration in milliseconds (default: 20ms)
    /// </summary>
    public int ChunkDurationMs { get; set; } = 20;

    /// <summary>
    /// Whether to capture loopback audio (system output) instead of microphone
    /// </summary>
    public bool CaptureLoopback { get; set; } = true;
}
