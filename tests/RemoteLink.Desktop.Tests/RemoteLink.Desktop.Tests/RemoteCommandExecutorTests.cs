using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Tests;

public class RemoteCommandExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenCommandSucceeds_ReturnsCapturedOutput()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var executor = new RemoteCommandExecutor();

        var result = await executor.ExecuteAsync(new RemoteCommandExecutionRequest
        {
            Shell = RemoteCommandShell.CommandPrompt,
            CommandText = "echo hello-remotelink"
        });

        Assert.True(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello-remotelink", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandFails_ReturnsNonZeroExitCodeAndError()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var executor = new RemoteCommandExecutor();

        var result = await executor.ExecuteAsync(new RemoteCommandExecutionRequest
        {
            Shell = RemoteCommandShell.CommandPrompt,
            CommandText = "dir C:\\path-that-does-not-exist-remotelink"
        });

        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandTimesOut_ReturnsTimedOutResult()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var executor = new RemoteCommandExecutor();

        var result = await executor.ExecuteAsync(new RemoteCommandExecutionRequest
        {
            Shell = RemoteCommandShell.PowerShell,
            CommandText = "Start-Sleep -Seconds 2",
            TimeoutSeconds = 1
        });

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("timed out", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }
}
