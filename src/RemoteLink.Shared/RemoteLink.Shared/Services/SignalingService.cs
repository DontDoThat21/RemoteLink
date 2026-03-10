using System.Net.Sockets;
using System.Text.Json;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Client for the central signaling directory that registers local devices and resolves remote device IDs.
/// </summary>
public sealed class SignalingService : ISignalingService, IDisposable
{
    private readonly SignalingConfiguration _configuration;
    private readonly ProxyConfiguration _proxyConfiguration;
    private readonly SemaphoreSlim _sync = new(1, 1);

    private DeviceInfo? _registeredDevice;
    private CancellationTokenSource? _refreshCts;
    private Task? _refreshTask;
    private bool _disposed;

    public SignalingService(SignalingConfiguration? configuration = null, ProxyConfiguration? proxyConfiguration = null)
    {
        _configuration = configuration ?? new SignalingConfiguration();
        _proxyConfiguration = proxyConfiguration ?? new ProxyConfiguration();
    }

    public bool IsConfigured => _configuration.IsConfigured;

    public async Task StartAsync(DeviceInfo localDevice, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(localDevice);

        if (!IsConfigured)
            return;

        await _sync.WaitAsync(cancellationToken);
        try
        {
            _registeredDevice = localDevice;
            if (_refreshTask is null)
            {
                _refreshCts = new CancellationTokenSource();
                _refreshTask = Task.Run(() => RefreshLoopAsync(_refreshCts.Token), _refreshCts.Token);
            }
        }
        finally
        {
            _sync.Release();
        }

        await RegisterDeviceAsync(localDevice, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var deviceId = _registeredDevice?.DeviceId;
            if (!string.IsNullOrWhiteSpace(deviceId) && IsConfigured)
            {
                try
                {
                    await ExchangeFrameAsync(new SignalingFrame
                    {
                        MessageType = "Unregister",
                        TargetDeviceId = deviceId
                    }, cancellationToken);
                }
                catch
                {
                }
            }

            var cts = _refreshCts;
            _refreshCts = null;
            _registeredDevice = null;

            if (cts is not null)
                cts.Cancel();

            if (_refreshTask is not null)
            {
                try { await _refreshTask; } catch { }
            }

            _refreshTask = null;
            cts?.Dispose();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task RegisterDeviceAsync(DeviceInfo localDevice, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(localDevice);

        if (!IsConfigured)
            return;

        var response = await ExchangeFrameAsync(new SignalingFrame
        {
            MessageType = "Register",
            Device = CloneDevice(localDevice)
        }, cancellationToken);

        if (response.Success && response.Device is not null)
        {
            localDevice.InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(response.Device.InternetDeviceId);
            localDevice.PublicIPAddress = response.Device.PublicIPAddress ?? localDevice.PublicIPAddress;
            localDevice.PublicPort = response.Device.PublicPort ?? localDevice.PublicPort;
        }
    }

    public async Task<DeviceInfo?> ResolveDeviceAsync(string deviceIdentifier, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceIdentifier);

        if (!IsConfigured)
            return null;

        var response = await ExchangeFrameAsync(new SignalingFrame
        {
            MessageType = "Lookup",
            TargetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(deviceIdentifier) ?? deviceIdentifier.Trim()
        }, cancellationToken);

        return response.Success && response.Device is not null
            ? CloneDevice(response.Device)
            : null;
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_configuration.RefreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var device = _registeredDevice;
                if (device is null)
                    continue;

                try
                {
                    await RegisterDeviceAsync(device, cancellationToken);
                }
                catch
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<SignalingFrame> ExchangeFrameAsync(SignalingFrame frame, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_configuration.ConnectTimeout);

        using var client = await ProxyTcpClientFactory.ConnectAsync(_configuration.ServerHost, _configuration.ServerPort, _proxyConfiguration, timeoutCts.Token);
        await using var stream = client.GetStream();
        await SendFrameAsync(stream, frame, timeoutCts.Token);
        return await ReadFrameAsync(stream, timeoutCts.Token)
            ?? new SignalingFrame { Success = false, ErrorMessage = "No response from signaling server." };
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

    private static async Task SendFrameAsync(Stream stream, SignalingFrame frame, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(frame);
        var length = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
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
            throw new ObjectDisposedException(nameof(SignalingService));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _sync.Dispose();
    }
}
