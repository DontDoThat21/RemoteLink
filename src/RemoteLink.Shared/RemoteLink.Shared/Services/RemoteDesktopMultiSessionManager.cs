using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Manages multiple simultaneous outgoing remote-control sessions.
/// </summary>
public sealed class RemoteDesktopMultiSessionManager
{
    private readonly Func<RemoteDesktopClient> _clientFactory;
    private readonly ILogger<RemoteDesktopMultiSessionManager> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly Dictionary<string, RemoteClientSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EventHandler<ClientConnectionState>> _sessionStateHandlers = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? SessionsChanged;

    public RemoteDesktopMultiSessionManager(
        Func<RemoteDesktopClient> clientFactory,
        ILogger<RemoteDesktopMultiSessionManager>? logger = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? NullLogger<RemoteDesktopMultiSessionManager>.Instance;
    }

    public async Task<RemoteClientSession> ConnectAsync(DeviceInfo host, string pin, CancellationToken cancellationToken = default)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        if (string.IsNullOrWhiteSpace(pin))
            throw new ArgumentException("PIN is required.", nameof(pin));

        var existing = await GetExistingSessionAsync(host, cancellationToken);
        if (existing is not null)
            return existing;

        var client = _clientFactory();
        string? failureReason = null;
        EventHandler<string> pairingFailedHandler = (_, reason) => failureReason = reason;
        client.PairingFailed += pairingFailedHandler;

        try
        {
            var connected = await client.ConnectToHostAsync(host, pin, cancellationToken);
            if (!connected)
                throw new InvalidOperationException(failureReason ?? $"Failed to connect to {host.DeviceName}.");

            var session = new RemoteClientSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                Host = host,
                Client = client,
                ConnectedAtUtc = DateTime.UtcNow
            };

            await _syncLock.WaitAsync(cancellationToken);
            try
            {
                existing = GetExistingSessionCore(host);
                if (existing is not null)
                {
                    try
                    {
                        await client.DisconnectAsync();
                    }
                    catch
                    {
                    }

                    if (client is IDisposable duplicateClient)
                        duplicateClient.Dispose();

                    return existing;
                }

                EventHandler<ClientConnectionState> stateHandler = (_, state) =>
                {
                    if (state == ClientConnectionState.Disconnected && !client.IsAutoReconnectPending)
                        _ = CloseSessionAsync(session.SessionId, disconnectClient: false);
                };

                client.ConnectionStateChanged += stateHandler;
                _sessions[session.SessionId] = session;
                _sessionStateHandlers[session.SessionId] = stateHandler;
            }
            finally
            {
                _syncLock.Release();
            }

            OnSessionsChanged();
            _logger.LogInformation("Opened remote session {SessionId} to {Host}", session.SessionId, host.DeviceName);
            return session;
        }
        catch
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync();
                }
                catch
                {
                }
            }

            if (client is IDisposable disposableClient)
                disposableClient.Dispose();

            throw;
        }
        finally
        {
            client.PairingFailed -= pairingFailedHandler;
        }
    }

    public IReadOnlyList<RemoteClientSession> GetSessions()
    {
        _syncLock.Wait();
        try
        {
            return _sessions.Values
                .OrderBy(session => session.ConnectedAtUtc)
                .ToList();
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<bool> CloseSessionAsync(string sessionId, bool disconnectClient = true)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        RemoteClientSession? session;
        EventHandler<ClientConnectionState>? handler = null;

        await _syncLock.WaitAsync();
        try
        {
            if (!_sessions.Remove(sessionId, out session))
                return false;

            if (_sessionStateHandlers.Remove(sessionId, out var removedHandler))
                handler = removedHandler;
        }
        finally
        {
            _syncLock.Release();
        }

        if (session is null)
            return false;

        if (handler is not null)
            session.Client.ConnectionStateChanged -= handler;

        if (disconnectClient)
        {
            try
            {
                await session.Client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting session {SessionId}", sessionId);
            }
        }

        if (session.Client is IDisposable disposableClient)
            disposableClient.Dispose();

        OnSessionsChanged();
        _logger.LogInformation("Closed remote session {SessionId}", sessionId);
        return true;
    }

    private async Task<RemoteClientSession?> GetExistingSessionAsync(DeviceInfo host, CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            return GetExistingSessionCore(host);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private RemoteClientSession? GetExistingSessionCore(DeviceInfo host)
    {
        var hostKey = GetHostKey(host);
        return _sessions.Values.FirstOrDefault(session => string.Equals(GetHostKey(session.Host), hostKey, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetHostKey(DeviceInfo host)
    {
        if (!string.IsNullOrWhiteSpace(host.InternetDeviceId))
            return $"internet:{DeviceIdentityManager.NormalizeInternetDeviceId(host.InternetDeviceId)}";

        if (!string.IsNullOrWhiteSpace(host.DeviceId))
            return $"device:{host.DeviceId}";

        return $"endpoint:{host.IPAddress}:{host.Port}";
    }

    private void OnSessionsChanged() => SessionsChanged?.Invoke(this, EventArgs.Empty);
}
