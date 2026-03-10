using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Lightweight TCP relay server that bridges two registered devices when direct transport paths fail.
/// </summary>
public sealed class RelayServer : IDisposable, IAsyncDisposable
{
    private sealed class RelayClientConnection
    {
        public required TcpClient TcpClient { get; init; }
        public required Stream Stream { get; init; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public string? DeviceId { get; set; }
        public string? InternetDeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string? PeerDeviceId { get; set; }
    }

    private sealed class RelayDeviceRegistryEntry
    {
        public string DeviceId { get; set; } = string.Empty;
        public string InternetDeviceId { get; set; } = string.Empty;
    }

    private readonly ConcurrentDictionary<string, RelayClientConnection> _registeredClients =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _internetDeviceIdsByDeviceId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _deviceIdsByInternetDeviceId =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<RelayClientConnection, byte> _connections = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _registryLock = new(1, 1);
    private readonly string _registryPath;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private bool _disposed;

    public RelayServer()
    {
        _registryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteLink",
            "relay_device_registry.json");
        LoadRegistry();
    }

    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_listener is not null)
                return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
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
            if (_listener is null)
                return;

            _cts?.Cancel();
            _listener.Stop();
            _listener = null;

            foreach (var connection in _connections.Keys)
                CloseConnection(connection, notifyPeer: false);

            if (_acceptLoopTask is not null)
            {
                try { await _acceptLoopTask; } catch { }
            }

            _cts?.Dispose();
            _cts = null;
            _acceptLoopTask = null;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                var connection = new RelayClientConnection
                {
                    TcpClient = client,
                    Stream = client.GetStream()
                };

                _connections[connection] = 0;
                _ = Task.Run(() => ClientLoopAsync(connection, cancellationToken), cancellationToken);
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

    private async Task ClientLoopAsync(RelayClientConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(connection.Stream, cancellationToken);
                if (frame is null)
                    break;

                await HandleFrameAsync(connection, frame, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            CloseConnection(connection, notifyPeer: true);
        }
    }

    private async Task HandleFrameAsync(RelayClientConnection connection, RelayFrame frame, CancellationToken cancellationToken)
    {
        switch (frame.MessageType)
        {
            case "Register":
                await HandleRegisterAsync(connection, frame, cancellationToken);
                break;

            case "Connect":
                await HandleConnectAsync(connection, frame, cancellationToken);
                break;

            case "Payload":
                await HandlePayloadAsync(connection, frame, cancellationToken);
                break;

            case "Disconnect":
                await CloseSessionAsync(connection, notifyPeer: true, cancellationToken);
                break;
        }
    }

    private async Task HandleRegisterAsync(RelayClientConnection connection, RelayFrame frame, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(frame.SourceDeviceId))
        {
            await SendFrameAsync(connection, new RelayFrame
            {
                MessageType = "RegisterAck",
                Success = false,
                ErrorMessage = "A device ID is required for relay registration."
            }, cancellationToken);
            return;
        }

        var assignedInternetDeviceId = await GetOrCreateInternetDeviceIdAsync(frame.SourceDeviceId);
        connection.DeviceId = frame.SourceDeviceId;
        connection.InternetDeviceId = assignedInternetDeviceId;
        connection.DeviceName = frame.Peer?.DeviceName ?? frame.SourceDeviceId;
        _registeredClients[frame.SourceDeviceId] = connection;

        await SendFrameAsync(connection, new RelayFrame
        {
            MessageType = "RegisterAck",
            Success = true,
            Peer = new RelayPeerInfo
            {
                DeviceId = frame.SourceDeviceId,
                InternetDeviceId = assignedInternetDeviceId,
                DeviceName = connection.DeviceName
            }
        }, cancellationToken);
    }

    private async Task HandleConnectAsync(RelayClientConnection source, RelayFrame frame, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.DeviceId))
        {
            await SendFrameAsync(source, new RelayFrame
            {
                MessageType = "ConnectAck",
                Success = false,
                ErrorMessage = "The source device is not registered with the relay."
            }, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(frame.TargetDeviceId) ||
            !TryResolveTargetConnection(frame.TargetDeviceId, out var target))
        {
            await SendFrameAsync(source, new RelayFrame
            {
                MessageType = "ConnectAck",
                Success = false,
                ErrorMessage = "The target device is not registered with the relay server."
            }, cancellationToken);
            return;
        }

        if (ReferenceEquals(source, target))
        {
            await SendFrameAsync(source, new RelayFrame
            {
                MessageType = "ConnectAck",
                Success = false,
                ErrorMessage = "The relay cannot connect a device to itself."
            }, cancellationToken);
            return;
        }

        await CloseSessionAsync(source, notifyPeer: true, cancellationToken);
        await CloseSessionAsync(target, notifyPeer: true, cancellationToken);

        var sessionId = Guid.NewGuid().ToString("N");
        source.SessionId = sessionId;
        source.PeerDeviceId = target.DeviceId;
        target.SessionId = sessionId;
        target.PeerDeviceId = source.DeviceId;

        await SendFrameAsync(source, new RelayFrame
        {
            MessageType = "ConnectAck",
            Success = true,
            SessionId = sessionId,
            TargetDeviceId = target.DeviceId,
            Peer = new RelayPeerInfo
            {
                DeviceId = target.DeviceId ?? string.Empty,
                InternetDeviceId = target.InternetDeviceId,
                DeviceName = target.DeviceName
            }
        }, cancellationToken);

        await SendFrameAsync(target, new RelayFrame
        {
            MessageType = "IncomingConnection",
            Success = true,
            SessionId = sessionId,
            SourceDeviceId = source.DeviceId,
            Peer = new RelayPeerInfo
            {
                DeviceId = source.DeviceId,
                InternetDeviceId = source.InternetDeviceId,
                DeviceName = source.DeviceName
            }
        }, cancellationToken);
    }

    private async Task HandlePayloadAsync(RelayClientConnection source, RelayFrame frame, CancellationToken cancellationToken)
    {
        if (!TryGetPeer(source, out var peer))
            return;

        await SendFrameAsync(peer, new RelayFrame
        {
            MessageType = "Payload",
            SessionId = source.SessionId,
            SourceDeviceId = source.DeviceId,
            TargetDeviceId = peer.DeviceId,
            Payload = frame.Payload,
            Success = true
        }, cancellationToken);
    }

    private bool TryGetPeer(RelayClientConnection connection, out RelayClientConnection peer)
    {
        peer = null!;

        if (string.IsNullOrWhiteSpace(connection.PeerDeviceId))
            return false;

        return _registeredClients.TryGetValue(connection.PeerDeviceId, out peer) &&
               string.Equals(peer.SessionId, connection.SessionId, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveTargetConnection(string targetDeviceIdentifier, out RelayClientConnection target)
    {
        if (_registeredClients.TryGetValue(targetDeviceIdentifier, out target!))
            return true;

        if (_deviceIdsByInternetDeviceId.TryGetValue(targetDeviceIdentifier, out var mappedDeviceId) &&
            _registeredClients.TryGetValue(mappedDeviceId, out target!))
        {
            return true;
        }

        target = null!;
        return false;
    }

    private async Task<string> GetOrCreateInternetDeviceIdAsync(string deviceId)
    {
        await _registryLock.WaitAsync();
        try
        {
            if (_internetDeviceIdsByDeviceId.TryGetValue(deviceId, out var existing))
                return existing;

            string internetDeviceId;
            do
            {
                internetDeviceId = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000)
                    .ToString("000000000", CultureInfo.InvariantCulture);
            }
            while (_deviceIdsByInternetDeviceId.ContainsKey(internetDeviceId));

            _internetDeviceIdsByDeviceId[deviceId] = internetDeviceId;
            _deviceIdsByInternetDeviceId[internetDeviceId] = deviceId;
            await SaveRegistryAsync();

            return internetDeviceId;
        }
        finally
        {
            _registryLock.Release();
        }
    }

    private void LoadRegistry()
    {
        try
        {
            if (!File.Exists(_registryPath))
                return;

            var entries = JsonSerializer.Deserialize<List<RelayDeviceRegistryEntry>>(File.ReadAllText(_registryPath));
            if (entries is null)
                return;

            foreach (var entry in entries)
            {
                var internetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(entry.InternetDeviceId);
                if (string.IsNullOrWhiteSpace(entry.DeviceId) || internetDeviceId is null)
                    continue;

                _internetDeviceIdsByDeviceId[entry.DeviceId] = internetDeviceId;
                _deviceIdsByInternetDeviceId[internetDeviceId] = entry.DeviceId;
            }
        }
        catch
        {
        }
    }

    private async Task SaveRegistryAsync()
    {
        var directory = Path.GetDirectoryName(_registryPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var entries = _internetDeviceIdsByDeviceId
            .Select(pair => new RelayDeviceRegistryEntry
            {
                DeviceId = pair.Key,
                InternetDeviceId = pair.Value
            })
            .OrderBy(entry => entry.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await File.WriteAllTextAsync(
            _registryPath,
            JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task CloseSessionAsync(RelayClientConnection connection, bool notifyPeer, CancellationToken cancellationToken)
    {
        RelayClientConnection? peer = null;
        if (notifyPeer)
            TryGetPeer(connection, out peer!);

        var sessionId = connection.SessionId;
        connection.SessionId = null;
        connection.PeerDeviceId = null;

        if (peer is not null)
        {
            peer.SessionId = null;
            peer.PeerDeviceId = null;
            await SendFrameAsync(peer, new RelayFrame
            {
                MessageType = "Disconnect",
                SessionId = sessionId,
                Success = true
            }, cancellationToken);
        }
    }

    private void CloseConnection(RelayClientConnection connection, bool notifyPeer)
    {
        _connections.TryRemove(connection, out _);

        if (!string.IsNullOrWhiteSpace(connection.DeviceId) &&
            _registeredClients.TryGetValue(connection.DeviceId, out var current) &&
            ReferenceEquals(current, connection))
        {
            _registeredClients.TryRemove(connection.DeviceId, out _);
        }

        if (notifyPeer)
        {
            try { CloseSessionAsync(connection, notifyPeer: true, CancellationToken.None).GetAwaiter().GetResult(); } catch { }
        }

        try { connection.Stream.Dispose(); } catch { }
        try { connection.TcpClient.Dispose(); } catch { }
        connection.WriteLock.Dispose();
    }

    private static async Task<RelayFrame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[sizeof(int)];
        var read = await ReadExactlyAsync(stream, lengthBuffer, cancellationToken);
        if (read == 0)
            return null;

        if (read != sizeof(int))
            return null;

        var length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length <= 0)
            return null;

        var payload = new byte[length];
        read = await ReadExactlyAsync(stream, payload, cancellationToken);
        if (read != length)
            return null;

        return JsonSerializer.Deserialize<RelayFrame>(payload);
    }

    private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                return offset;

            offset += read;
        }

        return offset;
    }

    private static byte[] SerializeFrame(RelayFrame frame) => JsonSerializer.SerializeToUtf8Bytes(frame);

    private static async Task SendFrameAsync(RelayClientConnection connection, RelayFrame frame, CancellationToken cancellationToken)
    {
        var payload = SerializeFrame(frame);
        var length = BitConverter.GetBytes(payload.Length);

        await connection.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await connection.Stream.WriteAsync(length, cancellationToken);
            await connection.Stream.WriteAsync(payload, cancellationToken);
            await connection.Stream.FlushAsync(cancellationToken);
        }
        finally
        {
            connection.WriteLock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RelayServer));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _lifecycleLock.Dispose();
        _registryLock.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
