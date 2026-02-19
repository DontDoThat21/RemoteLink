namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents clipboard data for network transmission.
/// </summary>
public class ClipboardData
{
    /// <summary>
    /// Type of clipboard content.
    /// </summary>
    public ClipboardContentType ContentType { get; set; }

    /// <summary>
    /// Text content (when ContentType is Text).
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Image data as PNG bytes (when ContentType is Image).
    /// </summary>
    public byte[]? ImageData { get; set; }

    /// <summary>
    /// Timestamp when this clipboard data was captured.
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Type of clipboard content.
/// </summary>
public enum ClipboardContentType
{
    /// <summary>No supported content.</summary>
    None = 0,

    /// <summary>Text content.</summary>
    Text = 1,

    /// <summary>Image content.</summary>
    Image = 2
}
