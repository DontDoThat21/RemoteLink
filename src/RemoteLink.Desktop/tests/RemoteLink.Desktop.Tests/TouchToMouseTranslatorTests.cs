using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Desktop.Tests;

/// <summary>
/// Unit tests for <see cref="TouchToMouseTranslator"/>.
/// Covers coordinate mapping, all gesture types, edge cases, and exceptions.
/// </summary>
public class TouchToMouseTranslatorTests
{
    private readonly TouchToMouseTranslator _sut = new();

    // ── Guard / exception tests ───────────────────────────────────────────────

    [Fact]
    public void Translate_NullGesture_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _sut.Translate(null!, 1920, 1080));
    }

    [Fact]
    public void Translate_ZeroTargetWidth_ThrowsArgumentOutOfRange()
    {
        var gesture = TapAt(0, 0, 400, 800);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _sut.Translate(gesture, 0, 1080));
    }

    [Fact]
    public void Translate_NegativeTargetWidth_ThrowsArgumentOutOfRange()
    {
        var gesture = TapAt(0, 0, 400, 800);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _sut.Translate(gesture, -1, 1080));
    }

    [Fact]
    public void Translate_ZeroTargetHeight_ThrowsArgumentOutOfRange()
    {
        var gesture = TapAt(0, 0, 400, 800);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _sut.Translate(gesture, 1920, 0));
    }

    // ── Coordinate mapping ────────────────────────────────────────────────────

    [Fact]
    public void Translate_Tap_TopLeftCorner_MapsToDesktopOrigin()
    {
        var gesture = TapAt(touchX: 0, touchY: 0, displayW: 400, displayH: 800);
        var events = _sut.Translate(gesture, 1920, 1080);

        var click = events[0];
        Assert.Equal(0, click.X);
        Assert.Equal(0, click.Y);
    }

    [Fact]
    public void Translate_Tap_BottomRightCorner_MapsToDesktopMax()
    {
        // Touch at the very bottom-right corner of a 400×800 display
        var gesture = TapAt(touchX: 400, touchY: 800, displayW: 400, displayH: 800);
        var events = _sut.Translate(gesture, 1920, 1080);

        var click = events[0];
        Assert.Equal(1919, click.X);
        Assert.Equal(1079, click.Y);
    }

    [Fact]
    public void Translate_Tap_Centre_MapsToDesktopCentre()
    {
        // Exact centre of a 400×800 display → centre of 1920×1080 desktop
        var gesture = TapAt(touchX: 200, touchY: 400, displayW: 400, displayH: 800);
        var events = _sut.Translate(gesture, 1920, 1080);

        var click = events[0];
        // 200/400 = 0.5 → 0.5 * 1919 = 959.5 → rounds to 960
        Assert.Equal(960, click.X);
        // 400/800 = 0.5 → 0.5 * 1079 = 539.5 → rounds to 540
        Assert.Equal(540, click.Y);
    }

    [Fact]
    public void Translate_Tap_OutOfBounds_ClampsToDesktopEdge()
    {
        // Touch beyond the display edges
        var gesture = TapAt(touchX: 999, touchY: 999, displayW: 400, displayH: 800);
        var events = _sut.Translate(gesture, 1920, 1080);

        var click = events[0];
        Assert.Equal(1919, click.X);   // clamped to max
        Assert.Equal(1079, click.Y);
    }

    [Fact]
    public void Translate_Tap_ZeroSizeDisplay_UsesTopLeft()
    {
        // Degenerate display with size 0 — should not divide by zero
        var gesture = new TouchGestureData
        {
            GestureType = TouchGestureType.Tap,
            X = 100, Y = 200,
            DisplayWidth = 0, DisplayHeight = 0
        };
        var events = _sut.Translate(gesture, 1920, 1080);

        Assert.Equal(0, events[0].X);
        Assert.Equal(0, events[0].Y);
    }

    [Fact]
    public void Translate_SmallTarget_QuarterTouch_RoundsCorrectly()
    {
        // Touch at 25% of a 200×400 display → 25% of 100×200 desktop
        var gesture = TapAt(touchX: 50, touchY: 100, displayW: 200, displayH: 400);
        var events = _sut.Translate(gesture, 100, 200);

        var click = events[0];
        // 50/200 = 0.25 → 0.25 * 99 = 24.75 → rounds to 25
        Assert.Equal(25, click.X);
        // 100/400 = 0.25 → 0.25 * 199 = 49.75 → rounds to 50
        Assert.Equal(50, click.Y);
    }

    // ── Tap → left click ──────────────────────────────────────────────────────

    [Fact]
    public void Translate_Tap_ReturnsTwoEvents()
    {
        var events = _sut.Translate(TapAt(100, 200, 400, 800), 1920, 1080);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Translate_Tap_FirstEvent_IsMouseClickDown()
    {
        var events = _sut.Translate(TapAt(100, 200, 400, 800), 1920, 1080);
        Assert.Equal(InputEventType.MouseClick, events[0].Type);
        Assert.True(events[0].IsPressed);
    }

    [Fact]
    public void Translate_Tap_SecondEvent_IsMouseClickUp()
    {
        var events = _sut.Translate(TapAt(100, 200, 400, 800), 1920, 1080);
        Assert.Equal(InputEventType.MouseClick, events[1].Type);
        Assert.False(events[1].IsPressed);
    }

    [Fact]
    public void Translate_Tap_BothEvents_HaveSameCoordinates()
    {
        var events = _sut.Translate(TapAt(100, 200, 400, 800), 1920, 1080);
        Assert.Equal(events[0].X, events[1].X);
        Assert.Equal(events[0].Y, events[1].Y);
    }

    // ── DoubleTap → double-click ──────────────────────────────────────────────

    [Fact]
    public void Translate_DoubleTap_ReturnsFourEvents()
    {
        var gesture = GestureAt(TouchGestureType.DoubleTap, 100, 200, 400, 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public void Translate_DoubleTap_AllEventsAreMouseClicks()
    {
        var gesture = GestureAt(TouchGestureType.DoubleTap, 100, 200, 400, 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.All(events, e => Assert.Equal(InputEventType.MouseClick, e.Type));
    }

    [Fact]
    public void Translate_DoubleTap_Pattern_IsDownUpDownUp()
    {
        var gesture = GestureAt(TouchGestureType.DoubleTap, 100, 200, 400, 800);
        var events = _sut.Translate(gesture, 1920, 1080);

        // down, up, down, up
        Assert.True(events[0].IsPressed);
        Assert.False(events[1].IsPressed);
        Assert.True(events[2].IsPressed);
        Assert.False(events[3].IsPressed);
    }

    [Fact]
    public void Translate_DoubleTap_AllEventsAtSameCoordinates()
    {
        var gesture = GestureAt(TouchGestureType.DoubleTap, 100, 200, 400, 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        int x = events[0].X, y = events[0].Y;
        Assert.All(events, e => { Assert.Equal(x, e.X); Assert.Equal(y, e.Y); });
    }

    // ── LongPress → right-click ───────────────────────────────────────────────

    [Fact]
    public void Translate_LongPress_ReturnsTwoEvents()
    {
        var gesture = GestureAt(TouchGestureType.LongPress, 100, 200, 400, 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Translate_LongPress_BothEventsAreMouseClick()
    {
        var gesture = GestureAt(TouchGestureType.LongPress, 100, 200, 400, 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.All(events, e => Assert.Equal(InputEventType.MouseClick, e.Type));
    }

    [Fact]
    public void Translate_LongPress_FirstEvent_IsPressedFalse_SignalsRightButton()
    {
        // Right-click down is indicated by IsPressed=false (first event)
        var gesture = GestureAt(TouchGestureType.LongPress, 100, 200, 400, 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.False(events[0].IsPressed);
    }

    [Fact]
    public void Translate_LongPress_MapsCoordinatesCorrectly()
    {
        var gesture = GestureAt(TouchGestureType.LongPress, 200, 400, 400, 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.Equal(960, events[0].X);
        Assert.Equal(540, events[0].Y);
    }

    // ── Pan → mouse move ──────────────────────────────────────────────────────

    [Fact]
    public void Translate_Pan_ReturnsOneEvent()
    {
        var gesture = GestureAt(TouchGestureType.Pan, 100, 200, 400, 800, deltaX: 5, deltaY: -3);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.Single(events);
    }

    [Fact]
    public void Translate_Pan_EventIsMouseMove()
    {
        var gesture = GestureAt(TouchGestureType.Pan, 100, 200, 400, 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.Equal(InputEventType.MouseMove, events[0].Type);
    }

    [Fact]
    public void Translate_Pan_UsesAbsolutePosition_NotDelta()
    {
        // Pan sends the current (absolute) touch position, not delta
        var gesture = GestureAt(TouchGestureType.Pan, 200, 400, 400, 800, deltaX: 999, deltaY: 999);
        var events = _sut.Translate(gesture, 1920, 1080);

        // Should be centre of desktop regardless of the large deltas
        Assert.Equal(960, events[0].X);
        Assert.Equal(540, events[0].Y);
    }

    // ── Scroll → mouse wheel ──────────────────────────────────────────────────

    [Fact]
    public void Translate_Scroll_ReturnsOneEvent()
    {
        var gesture = ScrollBy(deltaY: 10, displayW: 400, displayH: 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.Single(events);
    }

    [Fact]
    public void Translate_Scroll_EventIsMouseWheel()
    {
        var gesture = ScrollBy(deltaY: 10, displayW: 400, displayH: 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.Equal(InputEventType.MouseWheel, events[0].Type);
    }

    [Fact]
    public void Translate_ScrollDown_ProducesNegativeWheelDelta()
    {
        // Scroll DOWN (positive DeltaY) → negative wheel value (scroll down)
        var gesture = ScrollBy(deltaY: 10, displayW: 400, displayH: 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.True(events[0].Y < 0, $"Expected negative wheel delta, got {events[0].Y}");
    }

    [Fact]
    public void Translate_ScrollUp_ProducesPositiveWheelDelta()
    {
        // Scroll UP (negative DeltaY) → positive wheel value (scroll up)
        var gesture = ScrollBy(deltaY: -10, displayW: 400, displayH: 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.True(events[0].Y > 0, $"Expected positive wheel delta, got {events[0].Y}");
    }

    [Fact]
    public void Translate_Scroll_WheelDelta_ScalesWithPixelDelta()
    {
        var small = ScrollBy(deltaY: 5, displayW: 400, displayH: 800);
        var large = ScrollBy(deltaY: 10, displayW: 400, displayH: 800);

        var smallEvents = _sut.Translate(small, 1920, 1080);
        var largeEvents = _sut.Translate(large, 1920, 1080);

        // Larger DeltaY → larger magnitude wheel delta
        Assert.True(
            Math.Abs(largeEvents[0].Y) > Math.Abs(smallEvents[0].Y),
            "Larger scroll delta should produce larger wheel magnitude");
    }

    [Fact]
    public void Translate_Scroll_ZeroDelta_ProducesZeroWheelDelta()
    {
        var gesture = ScrollBy(deltaY: 0, displayW: 400, displayH: 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.Equal(0, events[0].Y);
    }

    [Fact]
    public void Translate_Scroll_WheelDeltaPerPixel_MatchesConstant()
    {
        // 1-pixel scroll: |Y| = 1 * WheelDeltaPerPixel
        var gesture = ScrollBy(deltaY: 1, displayW: 400, displayH: 800);
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.Equal(TouchToMouseTranslator.WheelDeltaPerPixel, Math.Abs(events[0].Y));
    }

    // ── Unknown gesture type ──────────────────────────────────────────────────

    [Fact]
    public void Translate_UnknownGestureType_ReturnsEmptyList()
    {
        var gesture = new TouchGestureData
        {
            GestureType = (TouchGestureType)999,
            X = 100, Y = 200,
            DisplayWidth = 400, DisplayHeight = 800
        };
        var events = _sut.Translate(gesture, 1920, 1080);
        Assert.Empty(events);
    }

    // ── Event metadata ────────────────────────────────────────────────────────

    [Fact]
    public void Translate_AllEvents_HaveNonNullEventId()
    {
        var events = _sut.Translate(TapAt(100, 200, 400, 800), 1920, 1080);
        Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e.EventId)));
    }

    [Fact]
    public void Translate_AllEvents_HaveRecentTimestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var events = _sut.Translate(TapAt(100, 200, 400, 800), 1920, 1080);
        Assert.All(events, e => Assert.True(e.Timestamp >= before));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TouchGestureData TapAt(
        float touchX, float touchY,
        float displayW, float displayH) =>
        new()
        {
            GestureType = TouchGestureType.Tap,
            X = touchX, Y = touchY,
            DisplayWidth = displayW, DisplayHeight = displayH
        };

    private static TouchGestureData GestureAt(
        TouchGestureType type,
        float touchX, float touchY,
        float displayW, float displayH,
        float deltaX = 0, float deltaY = 0) =>
        new()
        {
            GestureType = type,
            X = touchX, Y = touchY,
            DeltaX = deltaX, DeltaY = deltaY,
            DisplayWidth = displayW, DisplayHeight = displayH
        };

    private static TouchGestureData ScrollBy(float deltaY, float displayW, float displayH) =>
        new()
        {
            GestureType = TouchGestureType.Scroll,
            X = 200, Y = 400,          // position doesn't matter for wheel
            DeltaX = 0, DeltaY = deltaY,
            DisplayWidth = displayW, DisplayHeight = displayH
        };
}
