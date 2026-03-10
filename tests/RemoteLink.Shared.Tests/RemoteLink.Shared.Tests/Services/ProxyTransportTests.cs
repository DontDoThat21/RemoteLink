using System.Net;
using System.Net.Sockets;
using System.Text;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class ProxyTransportTests
{
    [Fact]
    public async Task ProxyTcpClientFactory_CanConnectThroughHttpConnectProxy()
    {
        await using var echoServer = new TcpEchoServer();
        await echoServer.StartAsync();
        await using var proxyServer = new HttpConnectProxyServer("proxy-user", "proxy-pass");
        await proxyServer.StartAsync();

        var proxyConfiguration = new ProxyConfiguration
        {
            Enabled = true,
            Type = ProxyType.Http,
            Host = "127.0.0.1",
            Port = proxyServer.Port,
            Username = "proxy-user",
            Password = "proxy-pass",
            ConnectTimeout = TimeSpan.FromSeconds(3)
        };

        using var client = await ProxyTcpClientFactory.ConnectAsync("127.0.0.1", echoServer.Port, proxyConfiguration);
        var stream = client.GetStream();
        var payload = Encoding.UTF8.GetBytes("hello through proxy");

        await stream.WriteAsync(payload);
        await stream.FlushAsync();

        var response = await ReadExactlyAsync(stream, payload.Length, CancellationToken.None);
        Assert.Equal(payload, response);
    }

    [Fact]
    public async Task SignalingService_CanRegisterAndResolveDeviceThroughSocks5Proxy()
    {
        await using var signalingServer = new SignalingServer();
        await signalingServer.StartAsync(0);
        await using var proxyServer = new Socks5ProxyServer("signal-user", "signal-pass");
        await proxyServer.StartAsync();

        var signalingConfiguration = new SignalingConfiguration
        {
            Enabled = true,
            ServerHost = "127.0.0.1",
            ServerPort = signalingServer.Port,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            RefreshInterval = TimeSpan.FromSeconds(30)
        };

        var proxyConfiguration = new ProxyConfiguration
        {
            Enabled = true,
            Type = ProxyType.Socks5,
            Host = "127.0.0.1",
            Port = proxyServer.Port,
            Username = "signal-user",
            Password = "signal-pass",
            ConnectTimeout = TimeSpan.FromSeconds(3)
        };

        var hostDevice = new DeviceInfo
        {
            DeviceId = "signal-proxy-host",
            DeviceName = "Signal Proxy Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop,
            SupportsRelay = true,
            RelayServerHost = "relay.example.test",
            RelayServerPort = 12400
        };

        using var registrar = new SignalingService(signalingConfiguration, proxyConfiguration);
        using var resolver = new SignalingService(signalingConfiguration, proxyConfiguration);

        await registrar.StartAsync(hostDevice);
        var resolved = await resolver.ResolveDeviceAsync(hostDevice.InternetDeviceId!);

        Assert.NotNull(resolved);
        Assert.Equal(hostDevice.DeviceId, resolved!.DeviceId);
        Assert.Equal(hostDevice.RelayServerHost, resolved.RelayServerHost);
        Assert.Equal(hostDevice.RelayServerPort, resolved.RelayServerPort);
    }

    [Fact]
    public void ProxyConfiguration_FromEnvironment_ParsesStandardProxyUri()
    {
        const string proxyUrl = "socks5://uri-user:uri-pass@127.0.0.1:1088";
        var previousValue = Environment.GetEnvironmentVariable("REMOTELINK_PROXY_URL");

        Environment.SetEnvironmentVariable("REMOTELINK_PROXY_URL", proxyUrl);

        try
        {
            var configuration = ProxyConfiguration.FromEnvironment();

            Assert.True(configuration.IsConfigured);
            Assert.Equal(ProxyType.Socks5, configuration.Type);
            Assert.Equal("127.0.0.1", configuration.Host);
            Assert.Equal(1088, configuration.Port);
            Assert.Equal("uri-user", configuration.Username);
            Assert.Equal("uri-pass", configuration.Password);
        }
        finally
        {
            Environment.SetEnvironmentVariable("REMOTELINK_PROXY_URL", previousValue);
        }
    }

    private sealed class TcpEchoServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private Task? _acceptTask;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public Task StartAsync()
        {
            _listener.Start();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[1024];
                while (!cancellationToken.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0)
                        break;

                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            if (_acceptTask is not null)
            {
                try { await _acceptTask; } catch { }
            }
            _cts.Dispose();
        }
    }

    private sealed class HttpConnectProxyServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly string? _username;
        private readonly string? _password;
        private Task? _acceptTask;

        public HttpConnectProxyServer(string? username = null, string? password = null)
        {
            _username = username;
            _password = password;
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public Task StartAsync()
        {
            _listener.Start();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                var clientStream = client.GetStream();
                var requestText = await ReadHttpHeadersAsync(clientStream, cancellationToken);
                var requestLines = requestText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                var connectLine = requestLines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (connectLine.Length < 2 || !string.Equals(connectLine[0], "CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteHttpResponseAsync(clientStream, "HTTP/1.1 405 Method Not Allowed\r\n\r\n", cancellationToken);
                    return;
                }

                if (!IsAuthorized(requestLines))
                {
                    await WriteHttpResponseAsync(clientStream, "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"RemoteLink\"\r\n\r\n", cancellationToken);
                    return;
                }

                ParseAuthority(connectLine[1], out var host, out var port);
                using var target = new TcpClient();
                await target.ConnectAsync(host, port, cancellationToken);
                await WriteHttpResponseAsync(clientStream, "HTTP/1.1 200 Connection Established\r\n\r\n", cancellationToken);

                await PumpBidirectionalAsync(clientStream, target.GetStream(), cancellationToken);
            }
        }

        private bool IsAuthorized(IEnumerable<string> requestLines)
        {
            if (string.IsNullOrWhiteSpace(_username) && string.IsNullOrWhiteSpace(_password))
                return true;

            var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username ?? string.Empty}:{_password ?? string.Empty}"));
            return requestLines.Any(line =>
                line.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase) &&
                line.Contains(expected, StringComparison.Ordinal));
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            if (_acceptTask is not null)
            {
                try { await _acceptTask; } catch { }
            }
            _cts.Dispose();
        }
    }

    private sealed class Socks5ProxyServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly string? _username;
        private readonly string? _password;
        private Task? _acceptTask;

        public Socks5ProxyServer(string? username = null, string? password = null)
        {
            _username = username;
            _password = password;
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public Task StartAsync()
        {
            _listener.Start();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                var clientStream = client.GetStream();

                var greeting = await ReadExactlyAsync(clientStream, 2, cancellationToken);
                var methods = await ReadExactlyAsync(clientStream, greeting[1], cancellationToken);
                var requiresAuth = !string.IsNullOrWhiteSpace(_username) || !string.IsNullOrWhiteSpace(_password);
                var selectedMethod = requiresAuth ? (byte)0x02 : (byte)0x00;
                if (!methods.Contains(selectedMethod))
                {
                    await clientStream.WriteAsync(new byte[] { 0x05, 0xFF }, cancellationToken);
                    return;
                }

                await clientStream.WriteAsync(new byte[] { 0x05, selectedMethod }, cancellationToken);

                if (selectedMethod == 0x02)
                {
                    var authHeader = await ReadExactlyAsync(clientStream, 2, cancellationToken);
                    var username = Encoding.UTF8.GetString(await ReadExactlyAsync(clientStream, authHeader[1], cancellationToken));
                    var passwordLength = (await ReadExactlyAsync(clientStream, 1, cancellationToken))[0];
                    var password = Encoding.UTF8.GetString(await ReadExactlyAsync(clientStream, passwordLength, cancellationToken));
                    var authSucceeded = string.Equals(username, _username, StringComparison.Ordinal) &&
                        string.Equals(password, _password, StringComparison.Ordinal);
                    await clientStream.WriteAsync(new byte[] { 0x01, authSucceeded ? (byte)0x00 : (byte)0x01 }, cancellationToken);
                    if (!authSucceeded)
                        return;
                }

                var requestHeader = await ReadExactlyAsync(clientStream, 4, cancellationToken);
                if (requestHeader[1] != 0x01)
                    return;

                var host = await ReadSocks5HostAsync(clientStream, requestHeader[3], cancellationToken);
                var portBytes = await ReadExactlyAsync(clientStream, 2, cancellationToken);
                var port = (portBytes[0] << 8) | portBytes[1];

                using var target = new TcpClient();
                await target.ConnectAsync(host, port, cancellationToken);
                await clientStream.WriteAsync(new byte[] { 0x05, 0x00, 0x00, 0x01, 127, 0, 0, 1, 0, 0 }, cancellationToken);

                await PumpBidirectionalAsync(clientStream, target.GetStream(), cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            if (_acceptTask is not null)
            {
                try { await _acceptTask; } catch { }
            }
            _cts.Dispose();
        }
    }

    private static async Task<string> ReadHttpHeadersAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                break;

            offset += read;
            if (offset >= 4 && buffer[offset - 4] == '\r' && buffer[offset - 3] == '\n' && buffer[offset - 2] == '\r' && buffer[offset - 1] == '\n')
                break;
        }

        return Encoding.ASCII.GetString(buffer, 0, offset);
    }

    private static async Task WriteHttpResponseAsync(Stream stream, string response, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static void ParseAuthority(string authority, out string host, out int port)
    {
        if (authority.StartsWith("[", StringComparison.Ordinal))
        {
            var end = authority.IndexOf(']');
            host = authority[1..end];
            port = int.Parse(authority[(end + 2)..]);
            return;
        }

        var separator = authority.LastIndexOf(':');
        host = authority[..separator];
        port = int.Parse(authority[(separator + 1)..]);
    }

    private static async Task<string> ReadSocks5HostAsync(Stream stream, byte addressType, CancellationToken cancellationToken)
    {
        return addressType switch
        {
            0x01 => new IPAddress(await ReadExactlyAsync(stream, 4, cancellationToken)).ToString(),
            0x03 => Encoding.ASCII.GetString(await ReadExactlyAsync(stream, (await ReadExactlyAsync(stream, 1, cancellationToken))[0], cancellationToken)),
            0x04 => new IPAddress(await ReadExactlyAsync(stream, 16, cancellationToken)).ToString(),
            _ => throw new InvalidOperationException("Unknown SOCKS5 address type.")
        };
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
                throw new IOException("Unexpected end of stream.");

            offset += read;
        }

        return buffer;
    }

    private static async Task PumpBidirectionalAsync(Stream left, Stream right, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leftToRight = PumpAsync(left, right, linkedCts.Token);
        var rightToLeft = PumpAsync(right, left, linkedCts.Token);

        await Task.WhenAny(leftToRight, rightToLeft);
        linkedCts.Cancel();

        try { await Task.WhenAll(leftToRight, rightToLeft); } catch { }
    }

    private static async Task PumpAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(destination, cancellationToken);
        }
        catch
        {
        }
        finally
        {
            try { destination.Dispose(); } catch { }
            try { source.Dispose(); } catch { }
        }
    }
}
