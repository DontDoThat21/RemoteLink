using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Records remote desktop sessions to video files.
/// Captures screen frames and optional audio into a standard video container (MP4).
/// </summary>
public interface ISessionRecorder
{
    /// <summary>
    /// Starts recording to the specified file path.
    /// </summary>
    /// <param name="filePath">Output video file path (e.g., "session.mp4").</param>
    /// <param name="frameRate">Target frame rate (default 15 FPS).</param>
    /// <param name="includeAudio">Whether to include audio in the recording (if available).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if recording started successfully, false otherwise.</returns>
    Task<bool> StartRecordingAsync(
        string filePath,
        int frameRate = 15,
        bool includeAudio = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the current recording and finalizes the video file.
    /// </summary>
    /// <returns>True if recording was stopped successfully, false if no recording was in progress.</returns>
    Task<bool> StopRecordingAsync();

    /// <summary>
    /// Pauses the current recording without finalizing the file.
    /// </summary>
    /// <returns>True if recording was paused, false if no recording was in progress.</returns>
    Task<bool> PauseRecordingAsync();

    /// <summary>
    /// Resumes a paused recording.
    /// </summary>
    /// <returns>True if recording was resumed, false if not paused.</returns>
    Task<bool> ResumeRecordingAsync();

    /// <summary>
    /// Writes a screen frame to the recording.
    /// No-op if recording is not active or is paused.
    /// </summary>
    /// <param name="screenData">Screen data to record.</param>
    Task WriteFrameAsync(ScreenData screenData);

    /// <summary>
    /// Writes an audio chunk to the recording.
    /// No-op if recording is not active, is paused, or audio is disabled.
    /// </summary>
    /// <param name="audioData">Audio data to record.</param>
    Task WriteAudioAsync(AudioData audioData);

    /// <summary>
    /// Gets whether a recording is currently in progress (started and not stopped).
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Gets whether the current recording is paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Gets the file path of the current recording, or null if not recording.
    /// </summary>
    string? CurrentFilePath { get; }

    /// <summary>
    /// Gets the duration of the current recording in seconds.
    /// </summary>
    TimeSpan RecordedDuration { get; }

    /// <summary>
    /// Fired when recording starts.
    /// </summary>
    event EventHandler<string>? RecordingStarted;

    /// <summary>
    /// Fired when recording stops.
    /// </summary>
    event EventHandler<string>? RecordingStopped;

    /// <summary>
    /// Fired when an error occurs during recording.
    /// </summary>
    event EventHandler<string>? RecordingError;
}
