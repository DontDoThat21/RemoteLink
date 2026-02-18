using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Tests;

/// <summary>
/// Unit tests for WindowsInputHandler.
/// These tests exercise lifecycle and routing logic; actual SendInput P/Invoke
/// calls are only made on Windows, so tests are safe to run on Linux CI.
/// </summary>
public class WindowsInputHandlerTests
{
    private WindowsInputHandler CreateHandler() =>
        new WindowsInputHandler(NullLogger<WindowsInputHandler>.Instance);

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_SetsIsActiveTrue()
    {
        var handler = CreateHandler();
        Assert.False(handler.IsActive);

        await handler.StartAsync();

        Assert.True(handler.IsActive);
    }

    [Fact]
    public async Task StopAsync_SetsIsActiveFalse()
    {
        var handler = CreateHandler();
        await handler.StartAsync();
        Assert.True(handler.IsActive);

        await handler.StopAsync();

        Assert.False(handler.IsActive);
    }

    [Fact]
    public async Task StartAsync_IsIdempotent()
    {
        var handler = CreateHandler();
        await handler.StartAsync();
        await handler.StartAsync(); // second call should not throw

        Assert.True(handler.IsActive);
    }

    // ── ProcessInputEventAsync: guard while inactive ───────────────────────────

    [Fact]
    public async Task ProcessInputEventAsync_WhenInactive_DoesNotThrow()
    {
        var handler = CreateHandler(); // not started

        var ev = new InputEvent { Type = InputEventType.MouseMove, X = 100, Y = 200 };
        var ex = await Record.ExceptionAsync(() => handler.ProcessInputEventAsync(ev));

        Assert.Null(ex);
    }

    // ── ProcessInputEventAsync: all event types complete without exception ─────

    [Theory]
    [InlineData(InputEventType.MouseMove)]
    [InlineData(InputEventType.MouseClick)]
    [InlineData(InputEventType.MouseWheel)]
    [InlineData(InputEventType.KeyPress)]
    [InlineData(InputEventType.KeyRelease)]
    [InlineData(InputEventType.TextInput)]
    public async Task ProcessInputEventAsync_AllKnownTypes_DoNotThrow(InputEventType type)
    {
        var handler = CreateHandler();
        await handler.StartAsync();

        var ev = new InputEvent
        {
            Type = type,
            X = 50,
            Y = 50,
            KeyCode = "A",
            IsPressed = true,
            Text = "hello"
        };

        var ex = await Record.ExceptionAsync(() => handler.ProcessInputEventAsync(ev));
        Assert.Null(ex);
    }

    [Fact]
    public async Task ProcessInputEventAsync_UnknownEventType_DoesNotThrow()
    {
        var handler = CreateHandler();
        await handler.StartAsync();

        var ev = new InputEvent { Type = (InputEventType)999 };
        var ex = await Record.ExceptionAsync(() => handler.ProcessInputEventAsync(ev));

        Assert.Null(ex);
    }

    // ── KeyPress with null/invalid keycode ────────────────────────────────────

    [Fact]
    public async Task ProcessInputEventAsync_NullKeyCode_DoesNotThrow()
    {
        var handler = CreateHandler();
        await handler.StartAsync();

        var ev = new InputEvent { Type = InputEventType.KeyPress, KeyCode = null };
        var ex = await Record.ExceptionAsync(() => handler.ProcessInputEventAsync(ev));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ProcessInputEventAsync_InvalidKeyCode_DoesNotThrow()
    {
        var handler = CreateHandler();
        await handler.StartAsync();

        var ev = new InputEvent { Type = InputEventType.KeyPress, KeyCode = "NOTAKEY_XYZ" };
        var ex = await Record.ExceptionAsync(() => handler.ProcessInputEventAsync(ev));

        Assert.Null(ex);
    }

    // ── TextInput edge cases ───────────────────────────────────────────────────

    [Fact]
    public async Task ProcessInputEventAsync_NullText_DoesNotThrow()
    {
        var handler = CreateHandler();
        await handler.StartAsync();

        var ev = new InputEvent { Type = InputEventType.TextInput, Text = null };
        var ex = await Record.ExceptionAsync(() => handler.ProcessInputEventAsync(ev));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ProcessInputEventAsync_EmptyText_DoesNotThrow()
    {
        var handler = CreateHandler();
        await handler.StartAsync();

        var ev = new InputEvent { Type = InputEventType.TextInput, Text = "" };
        var ex = await Record.ExceptionAsync(() => handler.ProcessInputEventAsync(ev));

        Assert.Null(ex);
    }

    // ── IInputHandler contract ─────────────────────────────────────────────────

    [Fact]
    public void Implements_IInputHandler()
    {
        var handler = CreateHandler();
        Assert.IsAssignableFrom<RemoteLink.Shared.Interfaces.IInputHandler>(handler);
    }
}
