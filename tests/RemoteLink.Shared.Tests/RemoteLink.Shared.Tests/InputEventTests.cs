using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Tests;

public class InputEventTests
{
    [Fact]
    public void InputEvent_ShouldCreateWithDefaults()
    {
        // Arrange & Act
        var inputEvent = new InputEvent();

        // Assert
        Assert.NotEmpty(inputEvent.EventId);
        Assert.True(inputEvent.Timestamp > DateTime.MinValue);
        Assert.Equal(InputEventType.MouseMove, inputEvent.Type);
        Assert.Equal(0, inputEvent.X);
        Assert.Equal(0, inputEvent.Y);
        Assert.Null(inputEvent.KeyCode);
        Assert.False(inputEvent.IsPressed);
        Assert.Null(inputEvent.Text);
    }

    [Fact]
    public void InputEvent_MouseClick_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var x = 100;
        var y = 200;
        var isPressed = true;

        // Act
        var inputEvent = new InputEvent
        {
            Type = InputEventType.MouseClick,
            X = x,
            Y = y,
            IsPressed = isPressed
        };

        // Assert
        Assert.Equal(InputEventType.MouseClick, inputEvent.Type);
        Assert.Equal(x, inputEvent.X);
        Assert.Equal(y, inputEvent.Y);
        Assert.Equal(isPressed, inputEvent.IsPressed);
    }

    [Fact]
    public void InputEvent_KeyPress_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var keyCode = "Enter";
        var isPressed = true;

        // Act
        var inputEvent = new InputEvent
        {
            Type = InputEventType.KeyPress,
            KeyCode = keyCode,
            IsPressed = isPressed
        };

        // Assert
        Assert.Equal(InputEventType.KeyPress, inputEvent.Type);
        Assert.Equal(keyCode, inputEvent.KeyCode);
        Assert.Equal(isPressed, inputEvent.IsPressed);
    }

    [Fact]
    public void InputEvent_TextInput_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var text = "Hello World!";

        // Act
        var inputEvent = new InputEvent
        {
            Type = InputEventType.TextInput,
            Text = text
        };

        // Assert
        Assert.Equal(InputEventType.TextInput, inputEvent.Type);
        Assert.Equal(text, inputEvent.Text);
    }
}