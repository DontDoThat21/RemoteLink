namespace RemoteLink.Shared.Models;

/// <summary>
/// The type of touch gesture that has been detected on the mobile client.
/// </summary>
public enum TouchGestureType
{
    /// <summary>Single finger tap — maps to a left mouse click.</summary>
    Tap,

    /// <summary>Two rapid taps in the same spot — maps to a double-click.</summary>
    DoubleTap,

    /// <summary>Finger held down without movement — maps to a right mouse click.</summary>
    LongPress,

    /// <summary>Single finger drag — maps to mouse move (with optional button held).</summary>
    Pan,

    /// <summary>Two-finger scroll gesture — maps to mouse wheel.</summary>
    Scroll
}

/// <summary>
/// Captures the raw touch/gesture data emitted by the mobile UI before it is
/// translated into desktop <see cref="InputEvent"/> objects.
/// </summary>
public class TouchGestureData
{
    /// <summary>The gesture that was detected.</summary>
    public TouchGestureType GestureType { get; set; }

    /// <summary>
    /// X position of the touch point inside the remote-viewer surface, in
    /// display (device-independent) pixels.  Origin is top-left.
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// Y position of the touch point inside the remote-viewer surface, in
    /// display (device-independent) pixels.  Origin is top-left.
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// Incremental X movement since the last <see cref="Pan"/> update, in
    /// display pixels.  Unused for non-pan gestures.
    /// </summary>
    public float DeltaX { get; set; }

    /// <summary>
    /// Incremental Y movement since the last <see cref="Pan"/> or
    /// <see cref="Scroll"/> update, in display pixels.  Positive = down,
    /// negative = up.
    /// </summary>
    public float DeltaY { get; set; }

    /// <summary>
    /// Width of the remote-viewer surface in display pixels.  Used to
    /// normalise coordinates to the desktop resolution.
    /// </summary>
    public float DisplayWidth { get; set; }

    /// <summary>
    /// Height of the remote-viewer surface in display pixels.  Used to
    /// normalise coordinates to the desktop resolution.
    /// </summary>
    public float DisplayHeight { get; set; }

    /// <summary>UTC timestamp when this gesture event was captured.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
