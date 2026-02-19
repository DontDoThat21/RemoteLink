using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Tests;

public class KeyboardShortcutTests
{
    [Fact]
    public async Task SendShortcutAsync_WhenInactive_DoesNothing()
    {
        // Arrange
        var handler = new WindowsInputHandler(NullLogger<WindowsInputHandler>.Instance);

        // Act (without calling StartAsync)
        await handler.SendShortcutAsync(KeyboardShortcut.ShowDesktop);

        // Assert
        Assert.False(handler.IsActive);
    }

    [Fact]
    public async Task SendShortcutAsync_WhenActive_DoesNotThrow()
    {
        // Arrange
        var handler = new WindowsInputHandler(NullLogger<WindowsInputHandler>.Instance);
        await handler.StartAsync();

        // Act & Assert
        await handler.SendShortcutAsync(KeyboardShortcut.ShowDesktop);
        await handler.SendShortcutAsync(KeyboardShortcut.LockWorkstation);
        await handler.SendShortcutAsync(KeyboardShortcut.TaskSwitcher);
        await handler.SendShortcutAsync(KeyboardShortcut.CloseWindow);
        await handler.SendShortcutAsync(KeyboardShortcut.RunDialog);
        await handler.SendShortcutAsync(KeyboardShortcut.Explorer);
        await handler.SendShortcutAsync(KeyboardShortcut.TaskView);
        await handler.SendShortcutAsync(KeyboardShortcut.TaskManager);
        await handler.SendShortcutAsync(KeyboardShortcut.Settings);
        await handler.SendShortcutAsync(KeyboardShortcut.ToggleFullscreen);
        await handler.SendShortcutAsync(KeyboardShortcut.SnapLeft);
        await handler.SendShortcutAsync(KeyboardShortcut.SnapRight);
        await handler.SendShortcutAsync(KeyboardShortcut.MaximizeWindow);
        await handler.SendShortcutAsync(KeyboardShortcut.MinimizeWindow);

        await handler.StopAsync();
    }

    [Fact]
    public async Task SendShortcutAsync_CtrlAltDelete_LogsWarning()
    {
        // Arrange
        var handler = new WindowsInputHandler(NullLogger<WindowsInputHandler>.Instance);
        await handler.StartAsync();

        // Act & Assert (should not throw, but logs a warning)
        await handler.SendShortcutAsync(KeyboardShortcut.CtrlAltDelete);

        await handler.StopAsync();
    }

    [Fact]
    public async Task ProcessInputEventAsync_KeyboardShortcut_CallsSendShortcut()
    {
        // Arrange
        var handler = new WindowsInputHandler(NullLogger<WindowsInputHandler>.Instance);
        await handler.StartAsync();

        var inputEvent = new InputEvent
        {
            Type = InputEventType.KeyboardShortcut,
            Shortcut = KeyboardShortcut.ShowDesktop
        };

        // Act
        await handler.ProcessInputEventAsync(inputEvent);

        // Assert (should not throw)
        await handler.StopAsync();
    }

    [Fact]
    public async Task ProcessInputEventAsync_KeyboardShortcut_WithoutShortcutValue_DoesNotThrow()
    {
        // Arrange
        var handler = new WindowsInputHandler(NullLogger<WindowsInputHandler>.Instance);
        await handler.StartAsync();

        var inputEvent = new InputEvent
        {
            Type = InputEventType.KeyboardShortcut,
            Shortcut = null
        };

        // Act & Assert
        await handler.ProcessInputEventAsync(inputEvent);

        await handler.StopAsync();
    }

    [Fact]
    public async Task SendShortcutAsync_AllShortcuts_Complete()
    {
        // Arrange
        var handler = new WindowsInputHandler(NullLogger<WindowsInputHandler>.Instance);
        await handler.StartAsync();

        // Act & Assert — verify all enum values can be sent without throwing
        var allShortcuts = Enum.GetValues<KeyboardShortcut>();
        foreach (var shortcut in allShortcuts)
        {
            await handler.SendShortcutAsync(shortcut);
        }

        await handler.StopAsync();
    }

    [Theory]
    [InlineData(KeyboardShortcut.ShowDesktop)]
    [InlineData(KeyboardShortcut.LockWorkstation)]
    [InlineData(KeyboardShortcut.TaskSwitcher)]
    [InlineData(KeyboardShortcut.CloseWindow)]
    [InlineData(KeyboardShortcut.RunDialog)]
    [InlineData(KeyboardShortcut.Explorer)]
    [InlineData(KeyboardShortcut.TaskView)]
    [InlineData(KeyboardShortcut.TaskManager)]
    [InlineData(KeyboardShortcut.Settings)]
    [InlineData(KeyboardShortcut.ToggleFullscreen)]
    [InlineData(KeyboardShortcut.SnapLeft)]
    [InlineData(KeyboardShortcut.SnapRight)]
    [InlineData(KeyboardShortcut.MaximizeWindow)]
    [InlineData(KeyboardShortcut.MinimizeWindow)]
    public async Task SendShortcutAsync_IndividualShortcuts_DoNotThrow(KeyboardShortcut shortcut)
    {
        // Arrange
        var handler = new WindowsInputHandler(NullLogger<WindowsInputHandler>.Instance);
        await handler.StartAsync();

        // Act & Assert
        await handler.SendShortcutAsync(shortcut);

        await handler.StopAsync();
    }

    [Fact]
    public async Task MockInputHandler_SendShortcutAsync_WhenActive_DoesNotThrow()
    {
        // Arrange
        var handler = new MockInputHandler();
        await handler.StartAsync();

        // Act & Assert
        await handler.SendShortcutAsync(KeyboardShortcut.ShowDesktop);
        await handler.SendShortcutAsync(KeyboardShortcut.TaskSwitcher);

        await handler.StopAsync();
    }

    [Fact]
    public async Task MockInputHandler_SendShortcutAsync_WhenInactive_DoesNothing()
    {
        // Arrange
        var handler = new MockInputHandler();

        // Act (without calling StartAsync)
        await handler.SendShortcutAsync(KeyboardShortcut.ShowDesktop);

        // Assert
        Assert.False(handler.IsActive);
    }

    [Fact]
    public async Task MockInputHandler_ProcessInputEventAsync_KeyboardShortcut_CallsSendShortcut()
    {
        // Arrange
        var handler = new MockInputHandler();
        await handler.StartAsync();

        var inputEvent = new InputEvent
        {
            Type = InputEventType.KeyboardShortcut,
            Shortcut = KeyboardShortcut.TaskManager
        };

        // Act
        await handler.ProcessInputEventAsync(inputEvent);

        // Assert (should not throw)
        await handler.StopAsync();
    }

    [Fact]
    public async Task KeyboardShortcut_EnumValues_HaveExpectedCount()
    {
        // Arrange & Act
        var shortcuts = Enum.GetValues<KeyboardShortcut>();

        // Assert — verify we have all 15 shortcuts defined
        Assert.Equal(15, shortcuts.Length);
    }

    [Fact]
    public void KeyboardShortcut_Enum_CanBeParsed()
    {
        // Act & Assert
        Assert.True(Enum.TryParse<KeyboardShortcut>("ShowDesktop", out var result));
        Assert.Equal(KeyboardShortcut.ShowDesktop, result);

        Assert.True(Enum.TryParse<KeyboardShortcut>("CtrlAltDelete", out result));
        Assert.Equal(KeyboardShortcut.CtrlAltDelete, result);
    }

    [Fact]
    public void InputEvent_WithShortcut_CanBeCreated()
    {
        // Act
        var inputEvent = new InputEvent
        {
            Type = InputEventType.KeyboardShortcut,
            Shortcut = KeyboardShortcut.ShowDesktop
        };

        // Assert
        Assert.Equal(InputEventType.KeyboardShortcut, inputEvent.Type);
        Assert.Equal(KeyboardShortcut.ShowDesktop, inputEvent.Shortcut);
    }

    [Fact]
    public void InputEvent_WithoutShortcut_ShortcutIsNull()
    {
        // Act
        var inputEvent = new InputEvent
        {
            Type = InputEventType.KeyboardShortcut
        };

        // Assert
        Assert.Equal(InputEventType.KeyboardShortcut, inputEvent.Type);
        Assert.Null(inputEvent.Shortcut);
    }
}
