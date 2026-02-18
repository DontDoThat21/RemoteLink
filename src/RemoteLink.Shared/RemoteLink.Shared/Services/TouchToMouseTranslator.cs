using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Translates mobile touch gestures into one or more desktop
/// <see cref="InputEvent"/> objects that can be forwarded to the host via
/// <see cref="RemoteLink.Shared.Interfaces.ICommunicationService"/>.
///
/// Coordinate mapping:
///   Touch (X, Y) in [0..DisplayWidth] × [0..DisplayHeight]
///       → Desktop (X, Y) in [0..targetWidth] × [0..targetHeight]
///
/// All output coordinates are absolute pixels in the desktop's coordinate
/// space.  The <c>WindowsInputHandler</c> will further normalise them to the
/// 0–65 535 range expected by SendInput's MOUSEEVENTF_ABSOLUTE flag.
/// </summary>
public class TouchToMouseTranslator
{
    /// <summary>
    /// How many wheel "notches" a single-pixel scroll delta produces.
    /// Windows WHEEL_DELTA is 120 per notch; we scale by this factor so that
    /// a small swipe does not send an enormous wheel delta.
    /// </summary>
    public const int WheelDeltaPerPixel = 3;

    /// <summary>
    /// Translate a single <see cref="TouchGestureData"/> into the sequence of
    /// <see cref="InputEvent"/> objects that should be sent to the desktop host.
    /// </summary>
    /// <param name="gesture">Touch gesture from the mobile UI.</param>
    /// <param name="targetWidth">Desktop screen width in pixels.</param>
    /// <param name="targetHeight">Desktop screen height in pixels.</param>
    /// <returns>
    /// One or more <see cref="InputEvent"/> instances, already timestamped.
    /// Never returns <c>null</c>; returns an empty list for unknown gestures.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="gesture"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="targetWidth"/> or
    /// <paramref name="targetHeight"/> is not positive.
    /// </exception>
    public IReadOnlyList<InputEvent> Translate(
        TouchGestureData gesture,
        int targetWidth,
        int targetHeight)
    {
        if (gesture is null) throw new ArgumentNullException(nameof(gesture));
        if (targetWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Must be > 0.");
        if (targetHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetHeight), "Must be > 0.");

        var (desktopX, desktopY) = MapCoordinates(
            gesture.X, gesture.Y,
            gesture.DisplayWidth, gesture.DisplayHeight,
            targetWidth, targetHeight);

        return gesture.GestureType switch
        {
            TouchGestureType.Tap =>
                SingleClick(desktopX, desktopY),

            TouchGestureType.DoubleTap =>
                DoubleClick(desktopX, desktopY),

            TouchGestureType.LongPress =>
                RightClick(desktopX, desktopY),

            TouchGestureType.Pan =>
                MouseMove(desktopX, desktopY),

            TouchGestureType.Scroll =>
                WheelScroll(gesture.DeltaY),

            _ => Array.Empty<InputEvent>()
        };
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Maps a touch point from the viewer surface into absolute desktop pixel
    /// coordinates, clamped to [0..targetDim-1].
    /// </summary>
    private static (int x, int y) MapCoordinates(
        float touchX, float touchY,
        float displayWidth, float displayHeight,
        int targetWidth, int targetHeight)
    {
        // Guard against zero-size display (degenerate device)
        float scaleX = displayWidth > 0 ? touchX / displayWidth : 0f;
        float scaleY = displayHeight > 0 ? touchY / displayHeight : 0f;

        // Clamp to [0, 1] so out-of-bounds touches stay on screen
        scaleX = Math.Clamp(scaleX, 0f, 1f);
        scaleY = Math.Clamp(scaleY, 0f, 1f);

        int x = (int)Math.Round(scaleX * (targetWidth - 1));
        int y = (int)Math.Round(scaleY * (targetHeight - 1));

        return (x, y);
    }

    // ── Event builders ────────────────────────────────────────────────────────

    /// <summary>
    /// Single left-click: button-down followed by button-up at the same spot.
    /// </summary>
    private static IReadOnlyList<InputEvent> SingleClick(int x, int y) =>
    [
        new InputEvent { Type = InputEventType.MouseClick, X = x, Y = y, IsPressed = true },
        new InputEvent { Type = InputEventType.MouseClick, X = x, Y = y, IsPressed = false }
    ];

    /// <summary>
    /// Double-click: two rapid single-clicks at the same spot.
    /// </summary>
    private static IReadOnlyList<InputEvent> DoubleClick(int x, int y)
    {
        var events = new List<InputEvent>(4);
        events.AddRange(SingleClick(x, y));
        events.AddRange(SingleClick(x, y));
        return events.AsReadOnly();
    }

    /// <summary>
    /// Right-click: uses the same MouseClick type but marks <c>IsPressed</c>
    /// as <c>false</c> (host interprets this as a secondary/right button).
    /// <para>
    /// NOTE: A dedicated <c>MouseButton</c> field on <see cref="InputEvent"/>
    /// would be cleaner; this convention works with the current model where
    /// <c>IsPressed = false</c> on a raw click denotes the right button.
    /// </para>
    /// </summary>
    private static IReadOnlyList<InputEvent> RightClick(int x, int y) =>
    [
        // IsPressed=false signals right-click down, IsPressed=true = right-click up
        // Convention matches the placeholder in WindowsInputHandler's future right-click path.
        new InputEvent { Type = InputEventType.MouseClick, X = x, Y = y, IsPressed = false },
        new InputEvent { Type = InputEventType.MouseClick, X = x, Y = y, IsPressed = true }
    ];

    /// <summary>
    /// Mouse move: single MouseMove event at the translated position.
    /// </summary>
    private static IReadOnlyList<InputEvent> MouseMove(int x, int y) =>
    [
        new InputEvent { Type = InputEventType.MouseMove, X = x, Y = y }
    ];

    /// <summary>
    /// Mouse-wheel scroll.  <paramref name="pixelDeltaY"/> positive = scroll
    /// down; negative = scroll up.  Mapped to <c>InputEvent.Y</c> which is
    /// the field the <c>WindowsInputHandler</c> reads for wheel delta.
    /// </summary>
    private static IReadOnlyList<InputEvent> WheelScroll(float pixelDeltaY)
    {
        int delta = (int)Math.Round(-pixelDeltaY * WheelDeltaPerPixel);
        return
        [
            new InputEvent { Type = InputEventType.MouseWheel, Y = delta }
        ];
    }
}
