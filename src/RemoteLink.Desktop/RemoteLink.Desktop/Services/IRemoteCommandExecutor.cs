using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Executes remote shell commands on the host machine for an authenticated session.
/// </summary>
public interface IRemoteCommandExecutor
{
    /// <summary>
    /// Run the requested command and capture its result.
    /// </summary>
    Task<RemoteCommandExecutionResult> ExecuteAsync(RemoteCommandExecutionRequest request, CancellationToken cancellationToken = default);
}
