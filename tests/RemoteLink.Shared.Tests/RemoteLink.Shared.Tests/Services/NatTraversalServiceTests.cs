using System.Net;
using System.Net.Sockets;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class NatTraversalServiceTests
{
    private sealed class FakeStunServer : IAsyncDisposable
    {
        private readonly UdpClient _udpClient;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serverTask;

        public int Port => ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;

        public FakeStunServer()
        {
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _serverTask = Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync(_cts.Token);
                    if (result.Buffer.Length < 20)
                        continue;

                    var response = BuildBindingResponse(result.Buffer.AsSpan(8, 12).ToArray(), result.RemoteEndPoint);
                    await _udpClient.SendAsync(response, response.Length, result.RemoteEndPoint);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private static byte[] BuildBindingResponse(byte[] transactionId, IPEndPoint remoteEndpoint)
        {
            var response = new byte[32];
            response[0] = 0x01;
            response[1] = 0x01;
            response[2] = 0x00;
            response[3] = 0x0C;
            response[4] = 0x21;
            response[5] = 0x12;
            response[6] = 0xA4;
            response[7] = 0x42;
            Buffer.BlockCopy(transactionId, 0, response, 8, 12);

            response[20] = 0x00;
            response[21] = 0x20;
            response[22] = 0x00;
            response[23] = 0x08;
            response[24] = 0x00;
            response[25] = 0x01;

            var xPort = (ushort)(remoteEndpoint.Port ^ 0x2112);
            response[26] = (byte)(xPort >> 8);
            response[27] = (byte)(xPort & 0xFF);

            var cookie = new byte[] { 0x21, 0x12, 0xA4, 0x42 };
            var addressBytes = remoteEndpoint.Address.MapToIPv4().GetAddressBytes();
            for (var i = 0; i < 4; i++)
                response[28 + i] = (byte)(addressBytes[i] ^ cookie[i]);

            return response;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _udpClient.Dispose();
            try { await _serverTask; } catch { }
            _cts.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_WithLoopbackStunServer_PublishesServerReflexiveCandidate()
    {
        await using var stunServer = new FakeStunServer();
        using var service = new NatTraversalService(options: new NatTraversalOptions
        {
            StunServers = new List<string> { $"127.0.0.1:{stunServer.Port}" },
            StunTimeout = TimeSpan.FromSeconds(1)
        });

        var result = await service.StartAsync(0);

        Assert.NotNull(result.PublicIPAddress);
        Assert.True(result.PublicPort > 0);
        Assert.NotEmpty(result.Candidates.Where(candidate => candidate.Type == NatCandidateType.Host));
        Assert.True(
            result.Candidates.Any(candidate => candidate.Type == NatCandidateType.ServerReflexive) ||
            result.Candidates.Any(candidate =>
                candidate.Type == NatCandidateType.Host &&
                string.Equals(candidate.IPAddress, result.PublicIPAddress, StringComparison.OrdinalIgnoreCase) &&
                candidate.Port == result.PublicPort),
            "Expected either a server-reflexive candidate or a host candidate matching the discovered public endpoint.");
    }

    [Fact]
    public async Task TryConnectAsync_WhenRemoteListenerRunning_EstablishesUdpHolePunch()
    {
        var options = new NatTraversalOptions
        {
            StunServers = new List<string>(),
            PunchTimeout = TimeSpan.FromSeconds(2),
            PunchInterval = TimeSpan.FromMilliseconds(100)
        };

        using var listener = new NatTraversalService(options: options);
        using var caller = new NatTraversalService(options: options);

        var listenerDiscovery = await listener.StartAsync(0);
        await caller.StartAsync(0);

        var result = await caller.TryConnectAsync(listenerDiscovery.Candidates);

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal(listenerDiscovery.LocalPort, result.RemotePort);
        Assert.NotNull(result.RemoteIPAddress);
    }
}
