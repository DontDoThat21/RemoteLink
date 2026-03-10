using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Lightweight TCP signaling server that coordinates internet-facing device lookups.
/// </summary>
public sealed class SignalingServer : IDisposable, IAsyncDisposable
{
    private sealed class SignalingDeviceRegistryEntry
    {
        public string DeviceId { get; set; } = string.Empty;
        public string InternetDeviceId { get; set; } = string.Empty;
    }

    private sealed class SignalingDeviceRecord
    {
        public required DeviceInfo Device { get; init; }
        public required DateTime ExpiresAtUtc { get; set; }
    }

    private readonly ConcurrentDictionary<string, SignalingDeviceRecord> _onlineDevicesByDeviceId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _internetDeviceIdsByDeviceId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _deviceIdsByInternetDeviceId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _registryLock = new(1, 1);
    private readonly string _registryPath;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private bool _disposed;

    public SignalingServer()
    {
        _registryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteLink",
            "signaling_device_registry.json");
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

            if (_acceptLoopTask is not null)
            {
                try { await _acceptLoopTask; } catch { }
            }

            _cts?.Dispose();
            _cts = null;
            _acceptLoopTask = null;
            _onlineDevicesByDeviceId.Clear();
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
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
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

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                var frame = await ReadFrameAsync(stream, cancellationToken);
                if (frame is null)
                    return;

                var remoteEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
                var response = await HandleFrameAsync(frame, remoteEndpoint, cancellationToken);
                await SendFrameAsync(stream, response, cancellationToken);
            }
            catch
            {
            }
        }
    }

    private async Task<SignalingFrame> HandleFrameAsync(
        SignalingFrame frame,
        IPEndPoint? remoteEndpoint,
        CancellationToken cancellationToken)
    {
        PruneExpiredDevices();

        return frame.MessageType switch
        {
            "Register" => await HandleRegisterAsync(frame, remoteEndpoint, cancellationToken),
            "Lookup" => HandleLookup(frame),
            "Unregister" => HandleUnregister(frame),
            _ => new SignalingFrame
            {
                MessageType = "Error",
                Success = false,
                ErrorMessage = "Unsupported signaling message type."
            }
        };
    }

    private async Task<SignalingFrame> HandleRegisterAsync(
        SignalingFrame frame,
        IPEndPoint? remoteEndpoint,
        CancellationToken cancellationToken)
    {
        if (frame.Device is null || string.IsNullOrWhiteSpace(frame.Device.DeviceId))
        {
            return new SignalingFrame
            {
                MessageType = "RegisterAck",
                Success = false,
                ErrorMessage = "A device ID is required for signaling registration."
            };
        }

        var internetDeviceId = await GetOrCreateInternetDeviceIdAsync(frame.Device.DeviceId, cancellationToken);
        var registeredDevice = CloneDevice(frame.Device);
        registeredDevice.InternetDeviceId = internetDeviceId;
        registeredDevice.LastSeen = DateTime.UtcNow;
        registeredDevice.IsOnline = true;

        if (remoteEndpoint is not null)
        {
            registeredDevice.PublicIPAddress ??= remoteEndpoint.Address.ToString();
            if (string.IsNullOrWhiteSpace(registeredDevice.IPAddress))
                registeredDevice.IPAddress = remoteEndpoint.Address.ToString();
        }

        _onlineDevicesByDeviceId[registeredDevice.DeviceId] = new SignalingDeviceRecord
        {
            Device = registeredDevice,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2)
        };

        return new SignalingFrame
        {
            MessageType = "RegisterAck",
            Success = true,
            Device = CloneDevice(registeredDevice)
        };
    }

    private SignalingFrame HandleLookup(SignalingFrame frame)
    {
        if (string.IsNullOrWhiteSpace(frame.TargetDeviceId))
        {
            return new SignalingFrame
            {
                MessageType = "LookupResult",
                Success = false,
                ErrorMessage = "A target device ID is required."
            };
        }

        if (!TryResolveOnlineDevice(frame.TargetDeviceId, out var device))
        {
            return new SignalingFrame
            {
                MessageType = "LookupResult",
                Success = false,
                ErrorMessage = "The requested device is not currently registered."
            };
        }

        return new SignalingFrame
        {
            MessageType = "LookupResult",
            Success = true,
            Device = CloneDevice(device)
        };
    }

    private SignalingFrame HandleUnregister(SignalingFrame frame)
    {
        var deviceId = frame.TargetDeviceId ?? frame.Device?.DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return new SignalingFrame
            {
                MessageType = "UnregisterAck",
                Success = false,
                ErrorMessage = "A device ID is required to unregister."
            };
        }

        _onlineDevicesByDeviceId.TryRemove(deviceId, out _);
        return new SignalingFrame
        {
            MessageType = "UnregisterAck",
            Success = true
        };
    }

    private bool TryResolveOnlineDevice(string deviceIdentifier, out DeviceInfo device)
    {
        device = null!;

        if (_onlineDevicesByDeviceId.TryGetValue(deviceIdentifier, out var record) && !IsExpired(record))
        {
            device = record.Device;
            return true;
        }

        var normalizedInternetId = DeviceIdentityManager.NormalizeInternetDeviceId(deviceIdentifier);
        if (normalizedInternetId is not null &&
            _deviceIdsByInternetDeviceId.TryGetValue(normalizedInternetId, out var mappedDeviceId) &&
            _onlineDevicesByDeviceId.TryGetValue(mappedDeviceId, out record) &&
            !IsExpired(record))
        {
            device = record.Device;
            return true;
        }

        return false;
    }

    private static bool IsExpired(SignalingDeviceRecord record)
        => record.ExpiresAtUtc <= DateTime.UtcNow;

    private void PruneExpiredDevices()
    {
        foreach (var pair in _onlineDevicesByDeviceId)
        {
            if (IsExpired(pair.Value))
                _onlineDevicesByDeviceId.TryRemove(pair.Key, out _);
        }
    }

    private async Task<string> GetOrCreateInternetDeviceIdAsync(string deviceId, CancellationToken cancellationToken)
    {
        await _registryLock.WaitAsync(cancellationToken);
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
            await SaveRegistryAsync(cancellationToken);
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

            var entries = JsonSerializer.Deserialize<List<SignalingDeviceRegistryEntry>>(File.ReadAllText(_registryPath));
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

    private async Task SaveRegistryAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_registryPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var entries = _internetDeviceIdsByDeviceId
            .Select(pair => new SignalingDeviceRegistryEntry
            {
                DeviceId = pair.Key,
                InternetDeviceId = pair.Value
            })
            .OrderBy(entry => entry.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await File.WriteAllTextAsync(
            _registryPath,
            JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private static DeviceInfo CloneDevice(DeviceInfo device)
    {
        return new DeviceInfo
        {
            DeviceId = device.DeviceId,
            InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId),
            DeviceName = device.DeviceName,
            IPAddress = device.IPAddress,
            MacAddress = device.MacAddress,
            Port = device.Port,
            PublicIPAddress = device.PublicIPAddress,
            PublicPort = device.PublicPort,
            NatType = device.NatType,
            NatCandidates = device.NatCandidates.Select(candidate => new NatEndpointCandidate
            {
                CandidateId = candidate.CandidateId,
                IPAddress = candidate.IPAddress,
                Port = candidate.Port,
                Protocol = candidate.Protocol,
                Type = candidate.Type,
                Priority = candidate.Priority,
                Source = candidate.Source
            }).ToList(),
            SupportsRelay = device.SupportsRelay,
            RelayServerHost = device.RelayServerHost,
            RelayServerPort = device.RelayServerPort,
            Type = device.Type,
            LastSeen = device.LastSeen,
            IsOnline = device.IsOnline
        };
    }

    private static async Task<SignalingFrame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
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

        return JsonSerializer.Deserialize<SignalingFrame>(payload);
    }

    private static async Task SendFrameAsync(Stream stream, SignalingFrame frame, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(frame);
        var length = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SignalingServer));
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
