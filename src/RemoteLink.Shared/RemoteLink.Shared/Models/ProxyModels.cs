using System.Globalization;

namespace RemoteLink.Shared.Models;

/// <summary>
/// Supported outbound proxy protocols for relay and signaling TCP connections.
/// </summary>
public enum ProxyType
{
    Http,
    Socks5
}

/// <summary>
/// Configures an optional outbound proxy for internet-facing transport connections.
/// </summary>
public sealed class ProxyConfiguration
{
    public bool Enabled { get; set; }
    public ProxyType Type { get; set; } = ProxyType.Http;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 8080;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(Host) &&
        Port is > 0 and <= 65535;

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Username) ||
        !string.IsNullOrWhiteSpace(Password);

    public static ProxyConfiguration FromEnvironment(
        string typeEnvironmentVariable = "REMOTELINK_PROXY_TYPE",
        string hostEnvironmentVariable = "REMOTELINK_PROXY_HOST",
        string portEnvironmentVariable = "REMOTELINK_PROXY_PORT",
        string usernameEnvironmentVariable = "REMOTELINK_PROXY_USERNAME",
        string passwordEnvironmentVariable = "REMOTELINK_PROXY_PASSWORD",
        string urlEnvironmentVariable = "REMOTELINK_PROXY_URL")
    {
        var explicitUrl = Environment.GetEnvironmentVariable(urlEnvironmentVariable)?.Trim();
        if (TryParseUriConfiguration(explicitUrl, out var uriConfiguration))
            return uriConfiguration;

        var explicitHost = Environment.GetEnvironmentVariable(hostEnvironmentVariable)?.Trim();
        var explicitType = Environment.GetEnvironmentVariable(typeEnvironmentVariable)?.Trim();

        if (!string.IsNullOrWhiteSpace(explicitHost) || !string.IsNullOrWhiteSpace(explicitType))
        {
            var type = TryParseType(explicitType, out var parsedType)
                ? parsedType
                : ProxyType.Http;

            var portText = Environment.GetEnvironmentVariable(portEnvironmentVariable);
            var hasPort = int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port);

            return new ProxyConfiguration
            {
                Enabled = !string.IsNullOrWhiteSpace(explicitHost),
                Type = type,
                Host = explicitHost ?? string.Empty,
                Port = hasPort ? port : GetDefaultPort(type),
                Username = Environment.GetEnvironmentVariable(usernameEnvironmentVariable),
                Password = Environment.GetEnvironmentVariable(passwordEnvironmentVariable)
            };
        }

        foreach (var candidate in new[] { "ALL_PROXY", "HTTPS_PROXY", "HTTP_PROXY" })
        {
            if (TryParseUriConfiguration(Environment.GetEnvironmentVariable(candidate)?.Trim(), out uriConfiguration))
                return uriConfiguration;
        }

        return new ProxyConfiguration();
    }

    private static bool TryParseUriConfiguration(string? value, out ProxyConfiguration configuration)
    {
        configuration = new ProxyConfiguration();
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        if (!TryParseType(uri.Scheme, out var type))
            return false;

        var userInfo = uri.UserInfo.Split(':', 2, StringSplitOptions.None);
        configuration = new ProxyConfiguration
        {
            Enabled = true,
            Type = type,
            Host = uri.Host,
            Port = uri.IsDefaultPort ? GetDefaultPort(type) : uri.Port,
            Username = userInfo.Length > 0 && !string.IsNullOrWhiteSpace(userInfo[0]) ? Uri.UnescapeDataString(userInfo[0]) : null,
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null
        };

        return true;
    }

    private static bool TryParseType(string? value, out ProxyType type)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "http":
                type = ProxyType.Http;
                return true;
            case "socks5":
            case "socks5h":
            case "socks":
                type = ProxyType.Socks5;
                return true;
            default:
                type = ProxyType.Http;
                return false;
        }
    }

    private static int GetDefaultPort(ProxyType type)
        => type == ProxyType.Socks5 ? 1080 : 8080;
}
