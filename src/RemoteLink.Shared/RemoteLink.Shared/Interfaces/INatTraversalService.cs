using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Provides STUN-based public endpoint discovery and UDP hole-punch connectivity checks.
/// </summary>
public interface INatTraversalService
{
    /// <summary>
    /// Gets whether the local UDP listener is currently active.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the latest NAT discovery snapshot, if one has been gathered.
    /// </summary>
    NatDiscoveryResult? CurrentDiscovery { get; }

    /// <summary>
    /// Starts the UDP listener on the specified local port and gathers local/public candidates.
    /// </summary>
    Task<NatDiscoveryResult> StartAsync(int localPort, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes candidate discovery while keeping the current listener active.
    /// </summary>
    Task<NatDiscoveryResult> RefreshCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the local UDP listener.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Attempts a direct UDP hole-punch connection against the supplied remote candidates.
    /// </summary>
    Task<NatTraversalConnectResult> TryConnectAsync(
        IEnumerable<NatEndpointCandidate> remoteCandidates,
        CancellationToken cancellationToken = default);
}
