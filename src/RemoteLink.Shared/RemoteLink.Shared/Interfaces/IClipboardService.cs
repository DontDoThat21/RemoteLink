using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Service for monitoring and managing clipboard content.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Raised when clipboard content changes.
    /// </summary>
    event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    /// <summary>
    /// Starts monitoring clipboard changes.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops monitoring clipboard changes.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current clipboard text content.
    /// </summary>
    /// <returns>Text content or null if clipboard doesn't contain text.</returns>
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the clipboard text content.
    /// </summary>
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current clipboard image content.
    /// </summary>
    /// <returns>Image data as PNG bytes or null if clipboard doesn't contain an image.</returns>
    Task<byte[]?> GetImageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the clipboard image content from PNG bytes.
    /// </summary>
    Task SetImageAsync(byte[] pngData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the service is currently monitoring clipboard changes.
    /// </summary>
    bool IsMonitoring { get; }
}

/// <summary>
/// Event args for clipboard change notifications.
/// </summary>
public class ClipboardChangedEventArgs : EventArgs
{
    /// <summary>
    /// Type of clipboard content that changed.
    /// </summary>
    public ClipboardContentType ContentType { get; init; }

    /// <summary>
    /// Text content (when ContentType is Text).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Image data as PNG bytes (when ContentType is Image).
    /// </summary>
    public byte[]? ImageData { get; init; }
}
