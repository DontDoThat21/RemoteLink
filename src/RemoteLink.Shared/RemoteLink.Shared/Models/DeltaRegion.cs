namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents a changed region in a delta frame
/// </summary>
public class DeltaRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int DataOffset { get; set; }
    public int DataLength { get; set; }
}
