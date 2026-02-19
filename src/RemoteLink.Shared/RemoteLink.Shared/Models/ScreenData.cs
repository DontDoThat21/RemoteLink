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
    
    /// <summary>
    /// True if this is a delta frame (only changed regions)
    /// </summary>
    public bool IsDelta { get; set; }
    
    /// <summary>
    /// ID of the reference frame for delta encoding
    /// </summary>
    public string? ReferenceFrameId { get; set; }
    
    /// <summary>
    /// Changed regions in delta frames (x, y, width, height, offset in ImageData)
    /// </summary>
    public List<DeltaRegion>? DeltaRegions { get; set; }
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