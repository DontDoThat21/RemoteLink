using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Shared NAT traversal service using STUN for server-reflexive endpoint discovery
/// and a lightweight ICE-style UDP probe exchange for hole punching.
/// </summary>
public sealed class NatTraversalService : INatTraversalService, IDisposable
{
    private sealed class PunchMessage
    {
        public string SessionId { get; set; } = string.Empty;
        public bool IsAcknowledgement { get; set; }
    }

    private readonly ILogger<NatTraversalService> _logger;
    private readonly NatTraversalOptions _options;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IPEndPoint>> _pendingPunches = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IPEndPoint>> _pendingStunRequests = new();

    private UdpClient? _udpClient;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private int _localPort;
    private bool _disposed;

    public NatTraversalService(
        ILogger<NatTraversalService>? logger = null,
        NatTraversalOptions? options = null)
    {
        _logger = logger ?? NullLogger<NatTraversalService>.Instance;
        _options = options ?? new NatTraversalOptions();
    }

    public bool IsRunning => _udpClient is not null;

    public NatDiscoveryResult? CurrentDiscovery { get; private set; }

    public async Task<NatDiscoveryResult> StartAsync(int localPort, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_udpClient is not null && _localPort == localPort)
                return await RefreshCandidatesCoreAsync(cancellationToken);

            await StopCoreAsync();

            _receiveCts = new CancellationTokenSource();
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, localPort));
            _udpClient.Client.ReceiveTimeout = Timeout.Infinite;
            _localPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);

            _logger.LogInformation("NAT traversal listener started on UDP {Port}", _localPort);
            return await RefreshCandidatesCoreAsync(cancellationToken);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<NatDiscoveryResult> RefreshCandidatesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_udpClient is null)
                throw new InvalidOperationException("NAT traversal has not been started.");

            return await RefreshCandidatesCoreAsync(cancellationToken);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<NatTraversalConnectResult> TryConnectAsync(
        IEnumerable<NatEndpointCandidate> remoteCandidates,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var candidates = remoteCandidates?
            .Where(candidate =>
                candidate is not null &&
                candidate.Port > 0 &&
                string.Equals(candidate.Protocol, "udp", StringComparison.OrdinalIgnoreCase) &&
                IPAddress.TryParse(candidate.IPAddress, out _))
            .OrderByDescending(candidate => candidate.Priority)
            .ToList() ?? new List<NatEndpointCandidate>();

        if (candidates.Count == 0)
            throw new InvalidOperationException("No valid remote UDP NAT candidates were supplied.");

        if (_udpClient is null)
            throw new InvalidOperationException("NAT traversal has not been started.");

        var sessionId = Guid.NewGuid().ToString("N");
        var startedAt = DateTime.UtcNow;
        var tcs = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingPunches[sessionId] = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.PunchTimeout);

        try
        {
            var sendTask = Task.Run(async () =>
            {
                while (!timeoutCts.IsCancellationRequested && !tcs.Task.IsCompleted)
                {
                    foreach (var candidate in candidates)
                    {
                        var endpoint = new IPEndPoint(IPAddress.Parse(candidate.IPAddress), candidate.Port);
                        await SendPunchMessageAsync(endpoint, new PunchMessage { SessionId = sessionId }, timeoutCts.Token);
                    }

                    await Task.Delay(_options.PunchInterval, timeoutCts.Token);
                }
            }, timeoutCts.Token);

            var remoteEndpoint = await tcs.Task.WaitAsync(timeoutCts.Token);
            timeoutCts.Cancel();

            try { await sendTask; } catch (OperationCanceledException) { }

            var matched = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.IPAddress, remoteEndpoint.Address.ToString(), StringComparison.OrdinalIgnoreCase) &&
                candidate.Port == remoteEndpoint.Port);

            return new NatTraversalConnectResult
            {
                Success = true,
                RemoteIPAddress = remoteEndpoint.Address.ToString(),
                RemotePort = remoteEndpoint.Port,
                MatchedCandidateType = matched?.Type,
                RoundTripTime = DateTime.UtcNow - startedAt
            };
        }
        catch (OperationCanceledException)
        {
            return new NatTraversalConnectResult
            {
                Success = false,
                FailureReason = "Timed out waiting for a UDP hole-punch acknowledgement."
            };
        }
        finally
        {
            _pendingPunches.TryRemove(sessionId, out _);
        }
    }

    private async Task<NatDiscoveryResult> RefreshCandidatesCoreAsync(CancellationToken cancellationToken)
    {
        var udpClient = _udpClient ?? throw new InvalidOperationException("NAT traversal has not been started.");
        var actualPort = ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port;
        _localPort = actualPort;

        var candidates = GatherHostCandidates(actualPort);
        var publicEndpoint = await DiscoverPublicEndpointAsync(cancellationToken);

        if (publicEndpoint is not null &&
            !candidates.Any(candidate =>
                string.Equals(candidate.IPAddress, publicEndpoint.Address.ToString(), StringComparison.OrdinalIgnoreCase) &&
                candidate.Port == publicEndpoint.Port))
        {
            candidates.Add(new NatEndpointCandidate
            {
                IPAddress = publicEndpoint.Address.ToString(),
                Port = publicEndpoint.Port,
                Type = NatCandidateType.ServerReflexive,
                Priority = 100,
                Source = "stun"
            });
        }

        var result = new NatDiscoveryResult
        {
            LocalPort = actualPort,
            PublicIPAddress = publicEndpoint?.Address.ToString(),
            PublicPort = publicEndpoint?.Port,
            Candidates = candidates,
            NatType = publicEndpoint is null
                ? NatTraversalType.Unknown
                : candidates.Any(candidate =>
                    candidate.Type == NatCandidateType.Host &&
                    string.Equals(candidate.IPAddress, publicEndpoint.Address.ToString(), StringComparison.OrdinalIgnoreCase) &&
                    candidate.Port == publicEndpoint.Port)
                    ? NatTraversalType.DirectPublicInternet
                    : NatTraversalType.BehindNat,
            DiscoveredAt = DateTime.UtcNow
        };

        CurrentDiscovery = result;
        return result;
    }

    private List<NatEndpointCandidate> GatherHostCandidates(int localPort)
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address)
            .Distinct()
            .Select((address, index) => new NatEndpointCandidate
            {
                IPAddress = address.ToString(),
                Port = localPort,
                Type = NatCandidateType.Host,
                Priority = 200 - index,
                Source = "local"
            })
            .ToList();

        if (candidates.Count == 0)
        {
            candidates.Add(new NatEndpointCandidate
            {
                IPAddress = IPAddress.Loopback.ToString(),
                Port = localPort,
                Type = NatCandidateType.Host,
                Priority = 1,
                Source = "loopback"
            });
        }

        return candidates;
    }

    private async Task<IPEndPoint?> DiscoverPublicEndpointAsync(CancellationToken cancellationToken)
    {
        foreach (var stunServer in _options.StunServers)
        {
            if (!TryParseStunServer(stunServer, out var endpoint))
                continue;

            try
            {
                var publicEndpoint = await SendStunBindingRequestAsync(endpoint, cancellationToken);
                if (publicEndpoint is not null)
                {
                    _logger.LogInformation("Discovered public endpoint {Address}:{Port} via STUN {Server}",
                        publicEndpoint.Address, publicEndpoint.Port, stunServer);
                    return publicEndpoint;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "STUN discovery failed for {Server}", stunServer);
            }
        }

        return null;
    }

    private async Task<IPEndPoint?> SendStunBindingRequestAsync(IPEndPoint stunServer, CancellationToken cancellationToken)
    {
        var udpClient = _udpClient ?? throw new InvalidOperationException("NAT traversal has not been started.");
        var transactionId = RandomNumberGenerator.GetBytes(12);
        var transactionKey = Convert.ToHexString(transactionId);
        var tcs = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingStunRequests[transactionKey] = tcs;

        try
        {
            var request = BuildStunBindingRequest(transactionId);
            await udpClient.SendAsync(request, request.Length, stunServer);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.StunTimeout);
            return await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _pendingStunRequests.TryRemove(transactionKey, out _);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var udpClient = _udpClient;
                if (udpClient is null)
                    return;

                var result = await udpClient.ReceiveAsync(cancellationToken);
                if (TryHandleStunResponse(result.Buffer))
                    continue;

                await HandlePunchMessageAsync(result.Buffer, result.RemoteEndPoint, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "NAT traversal receive loop error");
            }
        }
    }

    private bool TryHandleStunResponse(byte[] buffer)
    {
        if (!TryParseStunBindingResponse(buffer, out var transactionId, out var remoteEndpoint))
            return false;

        if (_pendingStunRequests.TryRemove(transactionId, out var tcs))
            tcs.TrySetResult(remoteEndpoint);

        return true;
    }

    private async Task HandlePunchMessageAsync(byte[] buffer, IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
    {
        PunchMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<PunchMessage>(buffer);
        }
        catch
        {
            return;
        }

        if (message is null || string.IsNullOrWhiteSpace(message.SessionId))
            return;

        if (_pendingPunches.TryGetValue(message.SessionId, out var pending))
            pending.TrySetResult(remoteEndpoint);

        if (!message.IsAcknowledgement)
        {
            await SendPunchMessageAsync(remoteEndpoint, new PunchMessage
            {
                SessionId = message.SessionId,
                IsAcknowledgement = true
            }, cancellationToken);
        }
    }

    private async Task SendPunchMessageAsync(IPEndPoint endpoint, PunchMessage message, CancellationToken cancellationToken)
    {
        var udpClient = _udpClient ?? throw new InvalidOperationException("NAT traversal has not been started.");
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        await udpClient.SendAsync(payload, payload.Length, endpoint);
    }

    private static byte[] BuildStunBindingRequest(byte[] transactionId)
    {
        var request = new byte[20];
        request[0] = 0x00;
        request[1] = 0x01;
        request[2] = 0x00;
        request[3] = 0x00;
        request[4] = 0x21;
        request[5] = 0x12;
        request[6] = 0xA4;
        request[7] = 0x42;
        Buffer.BlockCopy(transactionId, 0, request, 8, 12);
        return request;
    }

    private static bool TryParseStunBindingResponse(byte[] buffer, out string transactionId, out IPEndPoint remoteEndpoint)
    {
        transactionId = string.Empty;
        remoteEndpoint = new IPEndPoint(IPAddress.None, 0);

        if (buffer.Length < 20)
            return false;

        var messageType = (ushort)((buffer[0] << 8) | buffer[1]);
        if (messageType != 0x0101)
            return false;

        if (buffer[4] != 0x21 || buffer[5] != 0x12 || buffer[6] != 0xA4 || buffer[7] != 0x42)
            return false;

        transactionId = Convert.ToHexString(buffer.AsSpan(8, 12));
        var messageLength = (ushort)((buffer[2] << 8) | buffer[3]);
        var index = 20;
        var limit = Math.Min(buffer.Length, 20 + messageLength);

        while (index + 4 <= limit)
        {
            var attributeType = (ushort)((buffer[index] << 8) | buffer[index + 1]);
            var attributeLength = (ushort)((buffer[index + 2] << 8) | buffer[index + 3]);
            var attributeValueIndex = index + 4;

            if (attributeValueIndex + attributeLength > buffer.Length)
                break;

            if (attributeType == 0x0020 && attributeLength >= 8)
            {
                var family = buffer[attributeValueIndex + 1];
                if (family == 0x01)
                {
                    var port = (ushort)(((buffer[attributeValueIndex + 2] << 8) | buffer[attributeValueIndex + 3]) ^ 0x2112);
                    var cookie = new byte[] { 0x21, 0x12, 0xA4, 0x42 };
                    var addressBytes = new byte[4];
                    for (var i = 0; i < 4; i++)
                        addressBytes[i] = (byte)(buffer[attributeValueIndex + 4 + i] ^ cookie[i]);

                    remoteEndpoint = new IPEndPoint(new IPAddress(addressBytes), port);
                    return true;
                }
            }
            else if (attributeType == 0x0001 && attributeLength >= 8)
            {
                var family = buffer[attributeValueIndex + 1];
                if (family == 0x01)
                {
                    var port = (ushort)((buffer[attributeValueIndex + 2] << 8) | buffer[attributeValueIndex + 3]);
                    var addressBytes = buffer.AsSpan(attributeValueIndex + 4, 4).ToArray();
                    remoteEndpoint = new IPEndPoint(new IPAddress(addressBytes), port);
                    return true;
                }
            }

            index = attributeValueIndex + attributeLength;
            while (index % 4 != 0)
                index++;
        }

        return false;
    }

    private static bool TryParseStunServer(string value, out IPEndPoint endpoint)
    {
        endpoint = new IPEndPoint(IPAddress.None, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            return false;

        IPAddress? ipAddress = null;
        if (!IPAddress.TryParse(parts[0], out ipAddress))
        {
            try
            {
                ipAddress = Dns.GetHostAddresses(parts[0]).FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            }
            catch
            {
                ipAddress = null;
            }
        }

        if (ipAddress is null)
            return false;

        endpoint = new IPEndPoint(ipAddress, port);
        return true;
    }

    private async Task StopCoreAsync()
    {
        CurrentDiscovery = null;
        _localPort = 0;

        foreach (var pending in _pendingPunches)
            pending.Value.TrySetCanceled();
        _pendingPunches.Clear();

        foreach (var pending in _pendingStunRequests)
            pending.Value.TrySetCanceled();
        _pendingStunRequests.Clear();

        var receiveCts = _receiveCts;
        var receiveTask = _receiveTask;
        var udpClient = _udpClient;

        _receiveCts = null;
        _receiveTask = null;
        _udpClient = null;

        try { receiveCts?.Cancel(); } catch { }
        try { udpClient?.Close(); } catch { }
        udpClient?.Dispose();

        if (receiveTask is not null)
        {
            try { await receiveTask; } catch { }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NatTraversalService));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _lifecycleLock.Dispose();
    }
}
