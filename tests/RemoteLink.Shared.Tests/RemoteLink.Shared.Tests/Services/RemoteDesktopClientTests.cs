using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class RemoteDesktopClientTests
{
    private sealed class FakeNetworkDiscovery : INetworkDiscovery
    {
        public event EventHandler<DeviceInfo>? DeviceDiscovered;
        public event EventHandler<DeviceInfo>? DeviceLost;

        public Task StartBroadcastingAsync() => Task.CompletedTask;
        public Task StopBroadcastingAsync() => Task.CompletedTask;
        public Task StartListeningAsync() => Task.CompletedTask;
        public Task StopListeningAsync() => Task.CompletedTask;
        public Task<IEnumerable<DeviceInfo>> GetDiscoveredDevicesAsync() => Task.FromResult(Enumerable.Empty<DeviceInfo>());
    }

    private sealed class FakeCommunicationService : ICommunicationService
    {
        public bool IsConnected { get; private set; }
        public SessionControlRequest? LastSessionControlRequest { get; private set; }
        public SessionControlResponse? SessionControlResponseToSend { get; set; }
        public PairingResponse PairingResponseToSend { get; set; } = new()
        {
            Success = true,
            SessionToken = "session-token"
        };
        public List<InputEvent> SentInputEvents { get; } = new();

        public event EventHandler<ScreenData>? ScreenDataReceived;
        public event EventHandler<InputEvent>? InputEventReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<PairingRequest>? PairingRequestReceived;
        public event EventHandler<PairingResponse>? PairingResponseReceived;
        public event EventHandler<ConnectionQuality>? ConnectionQualityReceived;
        public event EventHandler<SessionControlRequest>? SessionControlRequestReceived;
        public event EventHandler<SessionControlResponse>? SessionControlResponseReceived;
        public event EventHandler<ClipboardData>? ClipboardDataReceived;
        public event EventHandler<FileTransferRequest>? FileTransferRequestReceived;
        public event EventHandler<FileTransferResponse>? FileTransferResponseReceived;
        public event EventHandler<FileTransferChunk>? FileTransferChunkReceived;
        public event EventHandler<FileTransferComplete>? FileTransferCompleteReceived;
        public event EventHandler<AudioData>? AudioDataReceived;
        public event EventHandler<ChatMessage>? ChatMessageReceived;
        public event EventHandler<string>? MessageReadReceived;
        public event EventHandler<PrintJob>? PrintJobReceived;
        public event EventHandler<PrintJobResponse>? PrintJobResponseReceived;
        public event EventHandler<PrintJobStatus>? PrintJobStatusReceived;

        public Task StartAsync(int port) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public Task<bool> ConnectToDeviceAsync(DeviceInfo device)
        {
            IsConnected = true;
            ConnectionStateChanged?.Invoke(this, true);
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            ConnectionStateChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }

        public Task SendScreenDataAsync(ScreenData screenData) => Task.CompletedTask;
        public Task SendInputEventAsync(InputEvent inputEvent)
        {
            SentInputEvents.Add(inputEvent);
            return Task.CompletedTask;
        }

        public Task SendPairingRequestAsync(PairingRequest request)
        {
            PairingResponseReceived?.Invoke(this, PairingResponseToSend);
            return Task.CompletedTask;
        }

        public Task SendPairingResponseAsync(PairingResponse response) => Task.CompletedTask;
        public Task SendConnectionQualityAsync(ConnectionQuality quality) => Task.CompletedTask;
        public Task SendSessionControlRequestAsync(SessionControlRequest request)
        {
            LastSessionControlRequest = request;

            var response = SessionControlResponseToSend ?? new SessionControlResponse
            {
                Success = true,
                AppliedQuality = request.Quality,
                AppliedImageFormat = request.ImageFormat,
                AppliedAudioEnabled = request.AudioEnabled
            };

            response.RequestId = request.RequestId;
            response.Command = request.Command;
            SessionControlResponseReceived?.Invoke(this, response);

            return Task.CompletedTask;
        }
        public Task SendSessionControlResponseAsync(SessionControlResponse response) => Task.CompletedTask;
        public Task SendClipboardDataAsync(ClipboardData clipboardData) => Task.CompletedTask;
        public Task SendFileTransferRequestAsync(FileTransferRequest request) => Task.CompletedTask;
        public Task SendFileTransferResponseAsync(FileTransferResponse response) => Task.CompletedTask;
        public Task SendFileTransferChunkAsync(FileTransferChunk chunk) => Task.CompletedTask;
        public Task SendFileTransferCompleteAsync(FileTransferComplete complete) => Task.CompletedTask;
        public Task SendAudioDataAsync(AudioData audioData) => Task.CompletedTask;
        public Task SendChatMessageAsync(ChatMessage message) => Task.CompletedTask;
        public Task SendMessageReadAsync(string messageId) => Task.CompletedTask;
        public Task SendPrintJobAsync(PrintJob printJob) => Task.CompletedTask;
        public Task SendPrintJobResponseAsync(PrintJobResponse response) => Task.CompletedTask;
        public Task SendPrintJobStatusAsync(PrintJobStatus status) => Task.CompletedTask;

        public void RaiseConnectionQuality(ConnectionQuality quality)
        {
            ConnectionQualityReceived?.Invoke(this, quality);
        }

        public void RaiseConnectionStateChanged(bool connected)
        {
            IsConnected = connected;
            ConnectionStateChanged?.Invoke(this, connected);
        }
    }

    private sealed class FakeNatTraversalService : INatTraversalService
    {
        public bool IsRunning { get; private set; }
        public NatDiscoveryResult? CurrentDiscovery { get; private set; }
        public int? StartedPort { get; private set; }

        public event EventHandler<NatDatagramReceivedEventArgs>? DatagramReceived;

        public Task<NatDiscoveryResult> StartAsync(int localPort, CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            StartedPort = localPort;
            CurrentDiscovery = new NatDiscoveryResult
            {
                LocalPort = localPort,
                PublicIPAddress = "198.51.100.10",
                PublicPort = localPort,
                NatType = NatTraversalType.BehindNat,
                Candidates = new List<NatEndpointCandidate>
                {
                    new()
                    {
                        IPAddress = "10.0.0.25",
                        Port = localPort,
                        Type = NatCandidateType.Host,
                        Priority = 200
                    },
                    new()
                    {
                        IPAddress = "198.51.100.10",
                        Port = localPort,
                        Type = NatCandidateType.ServerReflexive,
                        Priority = 100
                    }
                }
            };

            return Task.FromResult(CurrentDiscovery);
        }

        public Task<NatDiscoveryResult> RefreshCandidatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CurrentDiscovery!);

        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public Task<NatTraversalConnectResult> TryConnectAsync(IEnumerable<NatEndpointCandidate> remoteCandidates, CancellationToken cancellationToken = default)
            => Task.FromResult(new NatTraversalConnectResult { Success = true });

        public Task SendDatagramAsync(string remoteIPAddress, int remotePort, byte[] payload, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task ConnectToHostAsync_WhenQualityReceived_UpdatesCurrentConnectionQuality()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService();
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        var host = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        };

        var updatedTcs = new TaskCompletionSource<ConnectionQuality>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionQualityUpdated += (_, quality) => updatedTcs.TrySetResult(quality);

        var connected = await client.ConnectToHostAsync(host, "123456");
        Assert.True(connected);

        var quality = new ConnectionQuality
        {
            Fps = 22,
            Bandwidth = 1_750_000,
            Latency = 82,
            Rating = QualityRating.Good,
            Timestamp = DateTime.UtcNow
        };

        comm.RaiseConnectionQuality(quality);

        var received = await updatedTcs.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(QualityRating.Good, received.Rating);
        Assert.NotNull(client.CurrentConnectionQuality);
        Assert.Equal(22, client.CurrentConnectionQuality!.Fps);
        Assert.Equal(82, client.CurrentConnectionQuality.Latency);
        Assert.Equal(1_750_000, client.CurrentConnectionQuality.Bandwidth);
    }

    [Fact]
    public async Task ConnectToHostAsync_WhenHostReturnsPermissions_StoresCurrentSessionPermissions()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService
        {
            PairingResponseToSend = new PairingResponse
            {
                Success = true,
                SessionToken = "session-token",
                SessionPermissions = new SessionPermissionSet
                {
                    AllowRemoteInput = false,
                    AllowClipboardSync = false,
                    AllowFileTransfer = false,
                    AllowAudioStreaming = true,
                    AllowSessionControl = false
                }
            }
        };
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        var connected = await client.ConnectToHostAsync(new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        }, "123456");

        Assert.True(connected);
        Assert.NotNull(client.CurrentSessionPermissions);
        Assert.False(client.CurrentSessionPermissions!.AllowRemoteInput);
        Assert.False(client.CurrentSessionPermissions.AllowFileTransfer);
        Assert.False(client.CurrentSessionPermissions.AllowSessionControl);
    }

    [Fact]
    public async Task ExecuteRemoteCommandAsync_SendsExecuteCommandRequestAndReturnsCommandResult()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService
        {
            SessionControlResponseToSend = new SessionControlResponse
            {
                Success = true,
                CommandResult = new RemoteCommandExecutionResult
                {
                    Shell = RemoteCommandShell.PowerShell,
                    Succeeded = true,
                    ExitCode = 0,
                    StandardOutput = "test-output",
                    DurationMs = 42,
                    StartedAtUtc = DateTime.UtcNow.AddMilliseconds(-42),
                    CompletedAtUtc = DateTime.UtcNow
                }
            }
        };
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        var connected = await client.ConnectToHostAsync(new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        }, "123456");

        Assert.True(connected);

        var result = await client.ExecuteRemoteCommandAsync("Get-Date", RemoteCommandShell.PowerShell, timeoutSeconds: 15, workingDirectory: @"C:\Temp");

        Assert.NotNull(comm.LastSessionControlRequest);
        Assert.Equal(SessionControlCommand.ExecuteCommand, comm.LastSessionControlRequest!.Command);
        Assert.NotNull(comm.LastSessionControlRequest.CommandRequest);
        Assert.Equal("Get-Date", comm.LastSessionControlRequest.CommandRequest!.CommandText);
        Assert.Equal(RemoteCommandShell.PowerShell, comm.LastSessionControlRequest.CommandRequest.Shell);
        Assert.Equal(15, comm.LastSessionControlRequest.CommandRequest.TimeoutSeconds);
        Assert.Equal(@"C:\Temp", comm.LastSessionControlRequest.CommandRequest.WorkingDirectory);
        Assert.True(result.Succeeded);
        Assert.Equal("test-output", result.StandardOutput);
    }

    [Fact]
    public async Task RequestRemoteRebootAsync_SchedulesAutomaticReconnect_WhenSupported()
    {
        var discovery = new FakeNetworkDiscovery();
        var initialComm = new FakeCommunicationService
        {
            SessionControlResponseToSend = new SessionControlResponse
            {
                Success = true,
                Command = SessionControlCommand.RebootDevice,
                AutoReconnectSupported = true,
                ReconnectDelaySeconds = 1
            }
        };
        var reconnectComm = new FakeCommunicationService
        {
            PairingResponseToSend = new PairingResponse
            {
                Success = true,
                SessionToken = "reconnected-token"
            }
        };

        int factoryCalls = 0;
        var client = new RemoteDesktopClient(
            NullLogger<RemoteDesktopClient>.Instance,
            discovery,
            () => factoryCalls++ == 0 ? initialComm : reconnectComm);

        var host = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        };

        Assert.True(await client.ConnectToHostAsync(host, "123456"));

        var reconnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionStateChanged += (_, state) =>
        {
            if (state == ClientConnectionState.Connected && client.SessionToken == "reconnected-token")
                reconnectedTcs.TrySetResult(true);
        };

        var response = await client.RequestRemoteRebootAsync();

        Assert.True(response.AutoReconnectSupported);
        Assert.Equal(SessionControlCommand.RebootDevice, initialComm.LastSessionControlRequest?.Command);
        Assert.True(client.IsAutoReconnectPending);

        initialComm.RaiseConnectionStateChanged(false);

        await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(4));

        Assert.Equal("reconnected-token", client.SessionToken);
        Assert.False(client.IsAutoReconnectPending);
    }

    [Fact]
    public async Task SendInputEventAsync_DoesNotForward_WhenRemoteInputPermissionDisabled()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService
        {
            PairingResponseToSend = new PairingResponse
            {
                Success = true,
                SessionToken = "session-token",
                SessionPermissions = new SessionPermissionSet
                {
                    AllowRemoteInput = false
                }
            }
        };
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        await client.ConnectToHostAsync(new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        }, "123456");

        await client.SendInputEventAsync(new InputEvent { Type = InputEventType.MouseMove, X = 50, Y = 50 });

        Assert.Empty(comm.SentInputEvents);
    }

    [Fact]
    public async Task DisconnectAsync_ClearsCurrentConnectionQuality()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService();
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        var host = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        };

        var connected = await client.ConnectToHostAsync(host, "123456");
        Assert.True(connected);

        comm.RaiseConnectionQuality(new ConnectionQuality
        {
            Fps = 14,
            Bandwidth = 512_000,
            Latency = 180,
            Rating = QualityRating.Fair,
            Timestamp = DateTime.UtcNow
        });

        Assert.NotNull(client.CurrentConnectionQuality);

        await client.DisconnectAsync();

        Assert.Null(client.CurrentConnectionQuality);
        Assert.Equal(ClientConnectionState.Disconnected, client.ConnectionState);
    }

    [Fact]
    public async Task GetRemoteSystemInfoAsync_WhenConnected_ReturnsSystemInfoSnapshot()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService
        {
            SessionControlResponseToSend = new SessionControlResponse
            {
                Success = true,
                SystemInfo = new RemoteSystemInfo
                {
                    MachineName = "Host-1",
                    OperatingSystem = "Windows 11",
                    ProcessorName = "Ryzen Test",
                    LogicalProcessorCount = 16,
                    TotalMemoryBytes = 32L * 1024 * 1024 * 1024,
                    AvailableMemoryBytes = 20L * 1024 * 1024 * 1024,
                    UptimeSeconds = 3600
                }
            }
        };
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        var host = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        };

        Assert.True(await client.ConnectToHostAsync(host, "123456"));

        var info = await client.GetRemoteSystemInfoAsync();

        Assert.NotNull(comm.LastSessionControlRequest);
        Assert.Equal(SessionControlCommand.GetSystemInformation, comm.LastSessionControlRequest!.Command);
        Assert.Equal("Host-1", info.MachineName);
        Assert.Equal("Ryzen Test", info.ProcessorName);
        Assert.Same(info, client.CurrentRemoteSystemInfo);
    }

    [Fact]
    public async Task DisconnectAsync_ClearsCurrentRemoteSystemInfo()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService
        {
            SessionControlResponseToSend = new SessionControlResponse
            {
                Success = true,
                SystemInfo = new RemoteSystemInfo { MachineName = "Host-1" }
            }
        };
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        Assert.True(await client.ConnectToHostAsync(new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        }, "123456"));

        await client.GetRemoteSystemInfoAsync();
        Assert.NotNull(client.CurrentRemoteSystemInfo);

        await client.DisconnectAsync();

        Assert.Null(client.CurrentRemoteSystemInfo);
    }

    [Fact]
    public async Task SetRemoteImageFormatAsync_WhenConnected_SendsSessionControlRequest()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService();
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        var host = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        };

        Assert.True(await client.ConnectToHostAsync(host, "123456"));

        var appliedFormat = await client.SetRemoteImageFormatAsync(ScreenDataFormat.PNG);

        Assert.Equal(ScreenDataFormat.PNG, appliedFormat);
        Assert.NotNull(comm.LastSessionControlRequest);
        Assert.Equal(SessionControlCommand.SetImageFormat, comm.LastSessionControlRequest!.Command);
        Assert.Equal(ScreenDataFormat.PNG, comm.LastSessionControlRequest.ImageFormat);
    }

    [Fact]
    public async Task SetRemoteAudioEnabledAsync_WhenConnected_SendsSessionControlRequest()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService();
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        var host = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        };

        Assert.True(await client.ConnectToHostAsync(host, "123456"));

        var audioEnabled = await client.SetRemoteAudioEnabledAsync(false);

        Assert.False(audioEnabled);
        Assert.NotNull(comm.LastSessionControlRequest);
        Assert.Equal(SessionControlCommand.SetAudioEnabled, comm.LastSessionControlRequest!.Command);
        Assert.False(comm.LastSessionControlRequest.AudioEnabled);
    }

    [Fact]
    public async Task GetNatTraversalInfoAsync_WhenConfigured_UpdatesLocalDeviceMetadata()
    {
        var discovery = new FakeNetworkDiscovery();
        var natTraversal = new FakeNatTraversalService();
        var localDevice = new DeviceInfo
        {
            DeviceId = "mobile-1",
            DeviceName = "Phone",
            IPAddress = "10.0.0.25",
            Port = 12347,
            Type = DeviceType.Mobile
        };

        var client = new RemoteDesktopClient(
            NullLogger<RemoteDesktopClient>.Instance,
            discovery,
            () => new FakeCommunicationService(),
            natTraversal,
            localDevice);

        var result = await client.GetNatTraversalInfoAsync();

        Assert.NotNull(result);
        Assert.True(natTraversal.IsRunning);
        Assert.Equal(12347, natTraversal.StartedPort);
        Assert.Equal("198.51.100.10", localDevice.PublicIPAddress);
        Assert.Equal(12347, localDevice.PublicPort);
        Assert.Equal(NatTraversalType.BehindNat, localDevice.NatType);
        Assert.Contains(localDevice.NatCandidates, candidate => candidate.Type == NatCandidateType.ServerReflexive);
    }
}
