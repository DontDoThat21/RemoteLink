using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Broadcasts incoming connection request alerts over the local network.
/// </summary>
public sealed class LanConnectionRequestNotificationPublisher : IConnectionRequestNotificationPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<LanConnectionRequestNotificationPublisher> _logger;

    public LanConnectionRequestNotificationPublisher(ILogger<LanConnectionRequestNotificationPublisher> logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync(IncomingConnectionRequestAlert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        try
        {
            using var udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;

            var payload = JsonSerializer.SerializeToUtf8Bytes(alert, JsonOptions);
            await udpClient.SendAsync(
                payload,
                payload.Length,
                new IPEndPoint(IPAddress.Broadcast, IncomingConnectionRequestAlertProtocol.Port));

            _logger.LogDebug(
                "Broadcast incoming connection request alert for host {HostDeviceName} from {ClientDeviceName}",
                alert.HostDeviceName,
                alert.ClientDeviceName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to broadcast incoming connection request alert");
        }
    }
}
