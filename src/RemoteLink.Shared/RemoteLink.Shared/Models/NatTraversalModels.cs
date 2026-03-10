namespace RemoteLink.Shared.Models;

/// <summary>
/// Describes how a candidate endpoint was discovered for NAT traversal.
/// </summary>
public enum NatCandidateType
{
    Host,
    ServerReflexive,
    Relay
}

/// <summary>
/// High-level NAT classification for the current device.
/// </summary>
public enum NatTraversalType
{
    Unknown,
    DirectPublicInternet,
    BehindNat
}

/// <summary>
/// A reachable endpoint candidate that can participate in ICE-style connectivity checks.
/// </summary>
public sealed class NatEndpointCandidate
{
    public string CandidateId { get; set; } = Guid.NewGuid().ToString("N");
    public string IPAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = "udp";
    public NatCandidateType Type { get; set; }
    public int Priority { get; set; }
    public string? Source { get; set; }
}

/// <summary>
/// NAT traversal discovery snapshot for a local device.
/// </summary>
public sealed class NatDiscoveryResult
{
    public NatTraversalType NatType { get; set; } = NatTraversalType.Unknown;
    public int LocalPort { get; set; }
    public string? PublicIPAddress { get; set; }
    public int? PublicPort { get; set; }
    public List<NatEndpointCandidate> Candidates { get; set; } = new();
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    public bool HasUsableCandidates => Candidates.Count > 0;
}

/// <summary>
/// Result of a UDP hole-punch connectivity attempt.
/// </summary>
public sealed class NatTraversalConnectResult
{
    public bool Success { get; set; }
    public string? RemoteIPAddress { get; set; }
    public int? RemotePort { get; set; }
    public NatCandidateType? MatchedCandidateType { get; set; }
    public TimeSpan RoundTripTime { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>
/// Configures STUN and probe behavior for NAT traversal.
/// </summary>
public sealed class NatTraversalOptions
{
    public List<string> StunServers { get; set; } = new()
    {
        "stun.cloudflare.com:3478",
        "stun.l.google.com:19302",
        "stun1.l.google.com:19302"
    };

    public TimeSpan StunTimeout { get; set; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan PunchTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan PunchInterval { get; set; } = TimeSpan.FromMilliseconds(250);
}
