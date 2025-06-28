using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Mock input handler implementation for cross-platform compatibility
/// In production, this would be replaced with platform-specific implementations
/// </summary>
public class MockInputHandler : IInputHandler
{
    private bool _isActive;

    public bool IsActive => _isActive;

    public async Task StartAsync()
    {
        _isActive = true;
        Console.WriteLine("Input handler started (mock implementation)");
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _isActive = false;
        Console.WriteLine("Input handler stopped");
        await Task.CompletedTask;
    }

    public async Task ProcessInputEventAsync(InputEvent inputEvent)
    {
        if (!_isActive) return;

        Console.WriteLine($"Processing input event: {inputEvent.Type} at ({inputEvent.X}, {inputEvent.Y})");
        
        switch (inputEvent.Type)
        {
            case InputEventType.MouseMove:
                Console.WriteLine($"Mock: Moving mouse to ({inputEvent.X}, {inputEvent.Y})");
                break;

            case InputEventType.MouseClick:
                Console.WriteLine($"Mock: Mouse {(inputEvent.IsPressed ? "down" : "up")} at ({inputEvent.X}, {inputEvent.Y})");
                break;

            case InputEventType.KeyPress:
                Console.WriteLine($"Mock: Key {inputEvent.KeyCode} {(inputEvent.IsPressed ? "pressed" : "released")}");
                break;

            case InputEventType.TextInput:
                Console.WriteLine($"Mock: Text input: '{inputEvent.Text}'");
                break;

            case InputEventType.MouseWheel:
                Console.WriteLine("Mock: Mouse wheel scrolled");
                break;
        }

        await Task.CompletedTask;
    }
}