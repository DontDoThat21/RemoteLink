using System.Diagnostics;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Executes remote commands through PowerShell or Command Prompt and captures stdout/stderr.
/// </summary>
public sealed class RemoteCommandExecutor : IRemoteCommandExecutor
{
    private const int DefaultTimeoutSeconds = 30;
    private const int MaxTimeoutSeconds = 300;
    private const int MaxCapturedOutputLength = 16_000;

    public async Task<RemoteCommandExecutionResult> ExecuteAsync(RemoteCommandExecutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.CommandText))
            throw new ArgumentException("Command text is required.", nameof(request));

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory) && !Directory.Exists(request.WorkingDirectory))
            throw new DirectoryNotFoundException($"Working directory '{request.WorkingDirectory}' was not found.");

        int timeoutSeconds = Math.Clamp(request.TimeoutSeconds <= 0 ? DefaultTimeoutSeconds : request.TimeoutSeconds, 1, MaxTimeoutSeconds);
        var startedAtUtc = DateTime.UtcNow;

        using var process = new Process
        {
            StartInfo = BuildStartInfo(request)
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start the remote command process.");

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        bool timedOut = false;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            TryKillProcessTree(process);
            await process.WaitForExitAsync();
        }

        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;
        var completedAtUtc = DateTime.UtcNow;

        if (timedOut)
        {
            standardError = string.IsNullOrWhiteSpace(standardError)
                ? $"Command timed out after {timeoutSeconds} seconds."
                : standardError + Environment.NewLine + $"Command timed out after {timeoutSeconds} seconds.";
        }

        return new RemoteCommandExecutionResult
        {
            Shell = request.Shell,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory) ? null : request.WorkingDirectory,
            Succeeded = !timedOut && process.ExitCode == 0,
            TimedOut = timedOut,
            ExitCode = timedOut ? -1 : process.ExitCode,
            StandardOutput = TrimOutput(standardOutput),
            StandardError = TrimOutput(standardError),
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationMs = Math.Max(0, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds)
        };
    }

    private static ProcessStartInfo BuildStartInfo(RemoteCommandExecutionRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Shell == RemoteCommandShell.CommandPrompt ? "cmd.exe" : "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            startInfo.WorkingDirectory = request.WorkingDirectory;

        if (request.Shell == RemoteCommandShell.CommandPrompt)
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(request.CommandText);
        }
        else
        {
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(request.CommandText);
        }

        return startInfo;
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static string TrimOutput(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= MaxCapturedOutputLength)
            return value;

        return value[..MaxCapturedOutputLength] + Environment.NewLine + "...output truncated by host...";
    }
}
