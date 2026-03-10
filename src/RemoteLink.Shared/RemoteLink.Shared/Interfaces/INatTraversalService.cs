using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Provides STUN-based public endpoint discovery and UDP hole-punch connectivity checks.
/// </summary>
public interface INatTraversalService
{
    /// <summary>
    /// Fired when a non-STUN, non-hole-punch UDP payload is received on the NAT socket.
    /// </summary>
    event EventHandler<NatDatagramReceivedEventArgs>? DatagramReceived;

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

    /// <summary>
    /// Sends a raw UDP payload through the active NAT traversal socket.
    /// </summary>
    Task SendDatagramAsync(string remoteIPAddress, int remotePort, byte[] payload, CancellationToken cancellationToken = default);
}
