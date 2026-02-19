namespace RemoteLink.Shared.Models;

/// <summary>
/// Information about a display monitor
/// </summary>
public sealed class MonitorInfo
{
    /// <summary>
    /// Unique identifier for the monitor
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name of the monitor (e.g., "Display 1", "\\.\DISPLAY1")
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether this is the primary monitor
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Monitor width in pixels
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Monitor height in pixels
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Left position in virtual screen coordinates
    /// </summary>
    public int Left { get; init; }

    /// <summary>
    /// Top position in virtual screen coordinates
    /// </summary>
    public int Top { get; init; }

    /// <summary>
    /// Calculated right edge (Left + Width)
    /// </summary>
    public int Right => Left + Width;

    /// <summary>
    /// Calculated bottom edge (Top + Height)
    /// </summary>
    public int Bottom => Top + Height;
}
