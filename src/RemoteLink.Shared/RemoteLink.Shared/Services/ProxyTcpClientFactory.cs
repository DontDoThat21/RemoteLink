using System.Net;
using System.Net.Sockets;
using System.Text;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Creates TCP connections directly or through an HTTP CONNECT / SOCKS5 proxy.
/// </summary>
public static class ProxyTcpClientFactory
{
    public static async Task<TcpClient> ConnectAsync(
        string destinationHost,
        int destinationPort,
        ProxyConfiguration? proxyConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationHost);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(destinationPort);

        if (proxyConfiguration?.IsConfigured != true)
        {
            var directClient = new TcpClient();
            await directClient.ConnectAsync(destinationHost, destinationPort, cancellationToken);
            return directClient;
        }

        using var timeoutCts = proxyConfiguration.ConnectTimeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (timeoutCts is not null)
            timeoutCts.CancelAfter(proxyConfiguration.ConnectTimeout);

        var effectiveToken = timeoutCts?.Token ?? cancellationToken;
        var proxyClient = new TcpClient();

        try
        {
            await proxyClient.ConnectAsync(proxyConfiguration.Host, proxyConfiguration.Port, effectiveToken);
            var stream = proxyClient.GetStream();

            switch (proxyConfiguration.Type)
            {
                case ProxyType.Http:
                    await NegotiateHttpConnectAsync(stream, destinationHost, destinationPort, proxyConfiguration, effectiveToken);
                    break;
                case ProxyType.Socks5:
                    await NegotiateSocks5Async(stream, destinationHost, destinationPort, proxyConfiguration, effectiveToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported proxy type '{proxyConfiguration.Type}'.");
            }

            return proxyClient;
        }
        catch
        {
            proxyClient.Dispose();
            throw;
        }
    }

    private static async Task NegotiateHttpConnectAsync(
        NetworkStream stream,
        string destinationHost,
        int destinationPort,
        ProxyConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var authority = FormatAuthority(destinationHost, destinationPort);
        var builder = new StringBuilder()
            .Append("CONNECT ").Append(authority).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(authority).Append("\r\n")
            .Append("Proxy-Connection: Keep-Alive\r\n");

        if (configuration.HasCredentials)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{configuration.Username ?? string.Empty}:{configuration.Password ?? string.Empty}"));
            builder.Append("Proxy-Authorization: Basic ").Append(credentials).Append("\r\n");
        }

        builder.Append("\r\n");

        var request = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(request, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var response = await ReadHttpHeadersAsync(stream, cancellationToken);
        var statusLine = response.Split("\r\n", 2, StringSplitOptions.None)[0];
        if (!statusLine.Contains(" 200 ", StringComparison.Ordinal))
            throw new InvalidOperationException($"HTTP proxy CONNECT failed: {statusLine}");
    }

    private static async Task NegotiateSocks5Async(
        NetworkStream stream,
        string destinationHost,
        int destinationPort,
        ProxyConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var methods = configuration.HasCredentials ? new byte[] { 0x00, 0x02 } : new byte[] { 0x00 };
        var greeting = new byte[2 + methods.Length];
        greeting[0] = 0x05;
        greeting[1] = (byte)methods.Length;
        Array.Copy(methods, 0, greeting, 2, methods.Length);

        await stream.WriteAsync(greeting, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var greetingResponse = await ReadExactlyAsync(stream, 2, cancellationToken);
        if (greetingResponse[0] != 0x05)
            throw new InvalidOperationException("SOCKS5 proxy returned an invalid greeting response.");

        if (greetingResponse[1] == 0xFF)
            throw new InvalidOperationException("SOCKS5 proxy rejected all offered authentication methods.");

        if (greetingResponse[1] == 0x02)
            await AuthenticateSocks5Async(stream, configuration, cancellationToken);
        else if (greetingResponse[1] != 0x00)
            throw new InvalidOperationException($"SOCKS5 proxy selected unsupported auth method 0x{greetingResponse[1]:X2}.");

        var request = BuildSocks5ConnectRequest(destinationHost, destinationPort);
        await stream.WriteAsync(request, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var header = await ReadExactlyAsync(stream, 4, cancellationToken);
        if (header[0] != 0x05)
            throw new InvalidOperationException("SOCKS5 proxy returned an invalid connect response.");

        if (header[1] != 0x00)
            throw new InvalidOperationException($"SOCKS5 proxy connect failed with status 0x{header[1]:X2}.");

        var addressLength = header[3] switch
        {
            0x01 => 4,
            0x03 => (await ReadExactlyAsync(stream, 1, cancellationToken))[0],
            0x04 => 16,
            _ => throw new InvalidOperationException("SOCKS5 proxy returned an unknown address type.")
        };

        await ReadExactlyAsync(stream, addressLength + 2, cancellationToken);
    }

    private static async Task AuthenticateSocks5Async(NetworkStream stream, ProxyConfiguration configuration, CancellationToken cancellationToken)
    {
        var usernameBytes = Encoding.UTF8.GetBytes(configuration.Username ?? string.Empty);
        var passwordBytes = Encoding.UTF8.GetBytes(configuration.Password ?? string.Empty);

        if (usernameBytes.Length > byte.MaxValue || passwordBytes.Length > byte.MaxValue)
            throw new InvalidOperationException("SOCKS5 proxy credentials exceed the protocol length limit.");

        var authRequest = new byte[3 + usernameBytes.Length + passwordBytes.Length];
        authRequest[0] = 0x01;
        authRequest[1] = (byte)usernameBytes.Length;
        Array.Copy(usernameBytes, 0, authRequest, 2, usernameBytes.Length);
        authRequest[2 + usernameBytes.Length] = (byte)passwordBytes.Length;
        Array.Copy(passwordBytes, 0, authRequest, 3 + usernameBytes.Length, passwordBytes.Length);

        await stream.WriteAsync(authRequest, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var authResponse = await ReadExactlyAsync(stream, 2, cancellationToken);
        if (authResponse[1] != 0x00)
            throw new InvalidOperationException("SOCKS5 proxy authentication failed.");
    }

    private static byte[] BuildSocks5ConnectRequest(string destinationHost, int destinationPort)
    {
        if (IPAddress.TryParse(destinationHost, out var ipAddress))
        {
            var addressBytes = ipAddress.GetAddressBytes();
            var request = new byte[6 + addressBytes.Length];
            request[0] = 0x05;
            request[1] = 0x01;
            request[2] = 0x00;
            request[3] = ipAddress.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01;
            Array.Copy(addressBytes, 0, request, 4, addressBytes.Length);
            request[^2] = (byte)(destinationPort >> 8);
            request[^1] = (byte)(destinationPort & 0xFF);
            return request;
        }

        var hostBytes = Encoding.ASCII.GetBytes(destinationHost);
        if (hostBytes.Length > byte.MaxValue)
            throw new InvalidOperationException("SOCKS5 destination host is too long.");

        var requestForDomain = new byte[7 + hostBytes.Length];
        requestForDomain[0] = 0x05;
        requestForDomain[1] = 0x01;
        requestForDomain[2] = 0x00;
        requestForDomain[3] = 0x03;
        requestForDomain[4] = (byte)hostBytes.Length;
        Array.Copy(hostBytes, 0, requestForDomain, 5, hostBytes.Length);
        requestForDomain[^2] = (byte)(destinationPort >> 8);
        requestForDomain[^1] = (byte)(destinationPort & 0xFF);
        return requestForDomain;
    }

    private static async Task<string> ReadHttpHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                break;

            offset += read;
            if (offset >= 4 && HasHeaderTerminator(buffer, offset))
                return Encoding.ASCII.GetString(buffer, 0, offset);
        }

        throw new InvalidOperationException("HTTP proxy returned an incomplete response.");
    }

    private static bool HasHeaderTerminator(byte[] buffer, int length)
    {
        for (var i = 3; i < length; i++)
        {
            if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                return true;
        }

        return false;
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
                throw new IOException("The proxy closed the connection unexpectedly.");

            offset += read;
        }

        return buffer;
    }

    private static string FormatAuthority(string host, int port)
        => host.Contains(':', StringComparison.Ordinal) && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]:{port}"
            : $"{host}:{port}";
}
