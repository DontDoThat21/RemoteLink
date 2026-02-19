namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents captured audio data transmitted between devices
/// </summary>
public class AudioData
{
    /// <summary>
    /// Raw audio sample data (PCM format)
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Sample rate in Hz (e.g., 44100, 48000)
    /// </summary>
    public int SampleRate { get; set; }

    /// <summary>
    /// Number of audio channels (1 = mono, 2 = stereo)
    /// </summary>
    public int Channels { get; set; }

    /// <summary>
    /// Bits per sample (typically 16 or 32)
    /// </summary>
    public int BitsPerSample { get; set; }

    /// <summary>
    /// Timestamp when the audio was captured
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Duration of this audio chunk in milliseconds
    /// </summary>
    public int DurationMs { get; set; }

    /// <summary>
    /// Audio format identifier (e.g., "PCM", "Opus")
    /// </summary>
    public string Format { get; set; } = "PCM";
}
