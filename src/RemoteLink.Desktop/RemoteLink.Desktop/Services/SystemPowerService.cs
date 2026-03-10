using System.Diagnostics;

namespace RemoteLink.Desktop.Services;

public sealed class SystemPowerService : ISystemPowerService
{
    public Task RestartComputerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("shutdown", "/r /t 0 /f")
            : new ProcessStartInfo("shutdown", "-r now");

        startInfo.CreateNoWindow = true;
        startInfo.UseShellExecute = false;

        var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Failed to start the operating system reboot command.");

        return Task.CompletedTask;
    }
}
