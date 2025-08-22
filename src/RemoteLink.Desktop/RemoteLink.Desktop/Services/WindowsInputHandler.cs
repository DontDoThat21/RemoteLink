using System.Diagnostics;
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

            case InputEventType.CommandExecution:
                await ExecuteCommandAsync(inputEvent.Command, inputEvent.WorkingDirectory);
                break;

            case InputEventType.MouseWheel:
                Console.WriteLine("Mock: Mouse wheel scrolled");
                break;
        }

        await Task.CompletedTask;
    }

    private async Task ExecuteCommandAsync(string? command, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            Console.WriteLine("Command execution failed: Empty command");
            return;
        }

        try
        {
            Console.WriteLine($"Executing command: '{command}' in directory: '{workingDirectory ?? Environment.CurrentDirectory}'");

            ProcessStartInfo processStartInfo;
            
            if (OperatingSystem.IsWindows())
            {
                processStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                processStartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
        // Split command into command name and arguments
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
            Console.WriteLine($"Command execution failed: Command '{commandName}' is not allowed.");
            return;
        }

        try
        {
            Console.WriteLine($"Executing command: '{commandName} {commandArgs}' in directory: '{workingDirectory ?? Environment.CurrentDirectory}'");

            ProcessStartInfo processStartInfo = new ProcessStartInfo
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
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();

                Console.WriteLine($"Command execution completed with exit code: {process.ExitCode}");
                
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine($"Output:\n{output}");
                }

                // Add a timeout to prevent hanging processes
                int timeoutMilliseconds = 30000; // 30 seconds
                using (var cts = new System.Threading.CancellationTokenSource(timeoutMilliseconds))
                {
                    try
                    {
                        await process.WaitForExitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        try
                        {
                            process.Kill(true);
                        }
                        catch (Exception killEx)
                        {
                            Console.WriteLine($"Failed to kill process after timeout: {killEx.Message}");
                        }
                        Console.WriteLine($"Command execution timed out after {timeoutMilliseconds / 1000} seconds and was terminated.");
                        return;
                    }
                }

                Console.WriteLine($"Command execution completed with exit code: {process.ExitCode}");

                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine($"Output:\n{output}");
                }
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.WriteLine($"Error:\n{error}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Command execution failed: {ex.Message}");
        }
    }
}