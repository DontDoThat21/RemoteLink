using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Mobile.Services;

/// <summary>
/// Listens for LAN-wide incoming connection request alerts published by desktop hosts.
/// </summary>
public sealed class IncomingConnectionNotificationListener : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<IncomingConnectionNotificationListener> _logger;
    private readonly IAppSettingsService _settingsService;
    private readonly RemoteDesktopClient _client;
    private readonly ConcurrentDictionary<string, DateTime> _recentAlerts = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _started;

    public event EventHandler<IncomingConnectionRequestAlert>? NotificationReceived;

    public IncomingConnectionNotificationListener(
        ILogger<IncomingConnectionNotificationListener> logger,
        IAppSettingsService settingsService,
        RemoteDesktopClient client)
    {
        _logger = logger;
        _settingsService = settingsService;
        _client = client;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            return Task.CompletedTask;

        _started = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenTask = Task.Run(() => ListenAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null)
            return;

        try
        {
            await _cts.CancelAsync();
            if (_listenTask is not null)
                await _listenTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _listenTask = null;
            _started = false;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient(IncomingConnectionRequestAlertProtocol.Port);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync(cancellationToken);
                var alert = JsonSerializer.Deserialize<IncomingConnectionRequestAlert>(result.Buffer, JsonOptions);
                if (alert is null || string.IsNullOrWhiteSpace(alert.RequestId))
                    continue;

                CleanupRecentAlerts();
                if (!_recentAlerts.TryAdd(alert.RequestId, DateTime.UtcNow))
                    continue;

                if (!_settingsService.Current.General.ShowConnectionNotifications)
                    continue;

                if (IsSelfInitiatedRequest(alert))
                    continue;

                NotificationReceived?.Invoke(this, alert);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error receiving incoming connection notification");
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private bool IsSelfInitiatedRequest(IncomingConnectionRequestAlert alert)
    {
        var localClientName = $"{Environment.MachineName} (Mobile)";
        if (!string.Equals(alert.ClientDeviceName, localClientName, StringComparison.OrdinalIgnoreCase))
            return false;

        return _client.ConnectionState is ClientConnectionState.Connecting or ClientConnectionState.Authenticating
            && string.Equals(alert.HostDeviceName, _client.ConnectedHost?.DeviceName, StringComparison.OrdinalIgnoreCase);
    }

    private void CleanupRecentAlerts()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        foreach (var pair in _recentAlerts)
        {
            if (pair.Value < cutoff)
                _recentAlerts.TryRemove(pair.Key, out _);
        }
    }
}
