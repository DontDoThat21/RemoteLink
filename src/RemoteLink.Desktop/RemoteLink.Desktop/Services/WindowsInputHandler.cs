using System.Diagnostics;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Mock input handler that logs input events and executes allowlisted commands.
/// On Windows, a production implementation would use SendInput (user32.dll P/Invoke)
/// for real mouse/keyboard simulation.
/// </summary>
public class MockInputHandler : IInputHandler
{
    private bool _isActive;

    /// <summary>Allowlisted commands on Windows.</summary>
    private static readonly HashSet<string> AllowedCommandsWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        "echo", "dir", "type", "whoami", "hostname", "ipconfig", "ping", "netstat", "tasklist"
    };

    /// <summary>Allowlisted commands on Unix/Linux/macOS.</summary>
    private static readonly HashSet<string> AllowedCommandsUnix = new(StringComparer.OrdinalIgnoreCase)
    {
        "echo", "ls", "pwd", "date", "whoami", "hostname", "ifconfig", "ip", "ping", "netstat", "ps"
    };

    /// <inheritdoc/>
    public bool IsActive => _isActive;

    /// <inheritdoc/>
    public async Task StartAsync()
    {
        _isActive = true;
        Console.WriteLine("Input handler started");
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        _isActive = false;
        Console.WriteLine("Input handler stopped");
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
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

            case InputEventType.KeyRelease:
                Console.WriteLine($"Mock: Key {inputEvent.KeyCode} released");
                break;

            case InputEventType.TextInput:
                Console.WriteLine($"Mock: Text input: '{inputEvent.Text}'");
                break;

            case InputEventType.CommandExecution:
                await ExecuteCommandAsync(inputEvent.Command, inputEvent.WorkingDirectory);
                break;

            case InputEventType.MouseWheel:
                Console.WriteLine("Mock: Mouse wheel scrolled");
                break;

            default:
                Console.WriteLine($"Mock: Unknown input event type: {inputEvent.Type}");
                break;
        }
    }

    private async Task ExecuteCommandAsync(string? command, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            Console.WriteLine("Command execution failed: Empty command");
            return;
        }

        // Split into command name and arguments
        string[] parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Console.WriteLine("Command execution failed: Invalid command format");
            return;
        }

        string commandName = parts[0];
        string commandArgs = parts.Length > 1 ? parts[1] : string.Empty;

        bool isWindows = OperatingSystem.IsWindows();
        var allowedCommands = isWindows ? AllowedCommandsWindows : AllowedCommandsUnix;

        if (!allowedCommands.Contains(commandName))
        {
            Console.WriteLine($"Command execution failed: '{commandName}' is not in the allowlist.");
            return;
        }

        try
        {
            Console.WriteLine($"Executing: '{commandName} {commandArgs}' in '{workingDirectory ?? Environment.CurrentDirectory}'");

            var processStartInfo = new ProcessStartInfo
            {
                FileName = commandName,
                Arguments = commandArgs,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                Console.WriteLine("Command execution failed: Process could not be started");
                return;
            }

            const int TimeoutMs = 30_000; // 30 seconds
            using var cts = new CancellationTokenSource(TimeoutMs);

            // Read output concurrently to avoid deadlocks
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                Console.WriteLine($"Command '{commandName}' timed out after {TimeoutMs / 1000}s and was terminated.");
                return;
            }

            var output = await outputTask;
            var error = await errorTask;

            Console.WriteLine($"Command completed with exit code: {process.ExitCode}");

            if (!string.IsNullOrWhiteSpace(output))
                Console.WriteLine($"Output:\n{output}");

            if (!string.IsNullOrWhiteSpace(error))
                Console.WriteLine($"Stderr:\n{error}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Command execution failed: {ex.Message}");
        }
    }
}
