namespace RemoteLink.Shared.Models;

/// <summary>
/// Screen data sent from host to client
/// </summary>
public class ScreenData
{
    public string FrameId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public ScreenDataFormat Format { get; set; }
    public int Quality { get; set; } = 75; // JPEG quality for compression
}

/// <summary>
/// Format of screen data
/// </summary>
public enum ScreenDataFormat
{
    JPEG,
    PNG,
    Raw
}