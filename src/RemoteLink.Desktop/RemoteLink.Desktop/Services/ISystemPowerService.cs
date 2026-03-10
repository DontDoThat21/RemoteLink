namespace RemoteLink.Desktop.Services;

public interface ISystemPowerService
{
    Task RestartComputerAsync(CancellationToken cancellationToken = default);
}
