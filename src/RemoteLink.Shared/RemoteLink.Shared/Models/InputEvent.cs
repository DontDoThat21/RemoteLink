namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents input events (mouse/keyboard) sent from client to host
/// </summary>
public class InputEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public InputEventType Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string? KeyCode { get; set; }
    public bool IsPressed { get; set; }
    public string? Text { get; set; }
}

/// <summary>
/// Type of input event
/// </summary>
public enum InputEventType
{
    MouseMove,
    MouseClick,
    MouseWheel,
    KeyPress,
    KeyRelease,
    TextInput
}