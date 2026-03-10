using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// UDP-based communication service that rides on top of the NAT traversal socket,
/// including fragmentation/reassembly for large payloads.
/// </summary>
public sealed class UdpNatCommunicationService : ICommunicationService, IDisposable
{
    private sealed class NetworkMessage
    {
        public string MessageType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }

    private sealed class FragmentAccumulator
    {
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
        public string MessageType { get; init; } = string.Empty;
        public int FragmentCount { get; init; }
        public byte[][] Fragments { get; init; } = Array.Empty<byte[]>();
        public int ReceivedCount { get; set; }
    }

    private const string MsgTypeHello = "UdpHello";
    private const string MsgTypeHelloAck = "UdpHelloAck";
    private const string MsgTypeDisconnect = "UdpDisconnect";
    private const string MsgTypeScreen = "ScreenData";
    private const string MsgTypeInput = "InputEvent";
    private const string MsgTypePairingRequest = "PairingRequest";
    private const string MsgTypePairingResponse = "PairingResponse";
    private const string MsgTypeConnectionQuality = "ConnectionQuality";
    private const string MsgTypeSessionControlRequest = "SessionControlRequest";
    private const string MsgTypeSessionControlResponse = "SessionControlResponse";
    private const string MsgTypeClipboard = "ClipboardData";
    private const string MsgTypeFileTransferRequest = "FileTransferRequest";
    private const string MsgTypeFileTransferResponse = "FileTransferResponse";
    private const string MsgTypeFileTransferChunk = "FileTransferChunk";
    private const string MsgTypeFileTransferComplete = "FileTransferComplete";
    private const string MsgTypeAudio = "AudioData";
    private const string MsgTypeChatMessage = "ChatMessage";
    private const string MsgTypeMessageRead = "MessageRead";
    private const string MsgTypePrintJob = "PrintJob";
    private const string MsgTypePrintJobResponse = "PrintJobResponse";
    private const string MsgTypePrintJobStatus = "PrintJobStatus";

    private const int MaxFragmentPayloadBytes = 48 * 1024;
    private const int HeaderSize = 29;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RLNK");

    private readonly INatTraversalService _natTraversalService;
    private readonly ConcurrentDictionary<Guid, FragmentAccumulator> _incomingFragments = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly Timer _cleanupTimer;

    private TaskCompletionSource<bool>? _helloAckTcs;
    private IPEndPoint? _remoteEndpoint;
    private bool _started;
    private bool _disposed;

    public UdpNatCommunicationService(INatTraversalService natTraversalService)
    {
        _natTraversalService = natTraversalService ?? throw new ArgumentNullException(nameof(natTraversalService));
        _cleanupTimer = new Timer(_ => CleanupFragments(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool IsConnected => _remoteEndpoint is not null;

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

    public async Task StartAsync(int port)
    {
        ThrowIfDisposed();

        if (!_started)
        {
            _natTraversalService.DatagramReceived += OnDatagramReceived;
            _started = true;
        }

        await _natTraversalService.StartAsync(port);
    }

    public async Task StopAsync()
    {
        if (!_started)
            return;

        await DisconnectAsync();
        _natTraversalService.DatagramReceived -= OnDatagramReceived;
        _started = false;
        await _natTraversalService.StopAsync();
    }

    public async Task<bool> ConnectToDeviceAsync(DeviceInfo device)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(device);

        await _connectLock.WaitAsync();
        try
        {
            if (!_started)
            {
                _natTraversalService.DatagramReceived += OnDatagramReceived;
                _started = true;
            }

            if (!_natTraversalService.IsRunning)
                await _natTraversalService.StartAsync(0);

            var endpoint = await ResolveEndpointAsync(device);
            if (endpoint is null)
                return false;

            _remoteEndpoint = endpoint;
            _helloAckTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await SendControlMessageAsync(MsgTypeHello, new { DeviceId = device.DeviceId, DeviceName = device.DeviceName });

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                var acknowledged = await _helloAckTcs.Task.WaitAsync(timeoutCts.Token);
                if (acknowledged)
                {
                    ConnectionStateChanged?.Invoke(this, true);
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
            }

            _remoteEndpoint = null;
            return false;
        }
        finally
        {
            _helloAckTcs = null;
            _connectLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (_remoteEndpoint is not null)
        {
            try
            {
                await SendControlMessageAsync(MsgTypeDisconnect, string.Empty);
            }
            catch
            {
            }
        }

        if (_remoteEndpoint is not null)
        {
            _remoteEndpoint = null;
            ConnectionStateChanged?.Invoke(this, false);
        }
    }

    public Task SendScreenDataAsync(ScreenData screenData) => SendApplicationMessageAsync(MsgTypeScreen, screenData);
    public Task SendInputEventAsync(InputEvent inputEvent) => SendApplicationMessageAsync(MsgTypeInput, inputEvent);
    public Task SendPairingRequestAsync(PairingRequest request) => SendApplicationMessageAsync(MsgTypePairingRequest, request);
    public Task SendPairingResponseAsync(PairingResponse response) => SendApplicationMessageAsync(MsgTypePairingResponse, response);
    public Task SendConnectionQualityAsync(ConnectionQuality quality) => SendApplicationMessageAsync(MsgTypeConnectionQuality, quality);
    public Task SendSessionControlRequestAsync(SessionControlRequest request) => SendApplicationMessageAsync(MsgTypeSessionControlRequest, request);
    public Task SendSessionControlResponseAsync(SessionControlResponse response) => SendApplicationMessageAsync(MsgTypeSessionControlResponse, response);
    public Task SendClipboardDataAsync(ClipboardData clipboardData) => SendApplicationMessageAsync(MsgTypeClipboard, clipboardData);
    public Task SendFileTransferRequestAsync(FileTransferRequest request) => SendApplicationMessageAsync(MsgTypeFileTransferRequest, request);
    public Task SendFileTransferResponseAsync(FileTransferResponse response) => SendApplicationMessageAsync(MsgTypeFileTransferResponse, response);
    public Task SendFileTransferChunkAsync(FileTransferChunk chunk) => SendApplicationMessageAsync(MsgTypeFileTransferChunk, chunk);
    public Task SendFileTransferCompleteAsync(FileTransferComplete complete) => SendApplicationMessageAsync(MsgTypeFileTransferComplete, complete);
    public Task SendAudioDataAsync(AudioData audioData) => SendApplicationMessageAsync(MsgTypeAudio, audioData);
    public Task SendChatMessageAsync(ChatMessage message) => SendApplicationMessageAsync(MsgTypeChatMessage, message);
    public Task SendMessageReadAsync(string messageId) => SendApplicationMessageAsync(MsgTypeMessageRead, messageId);
    public Task SendPrintJobAsync(PrintJob printJob) => SendApplicationMessageAsync(MsgTypePrintJob, printJob);
    public Task SendPrintJobResponseAsync(PrintJobResponse response) => SendApplicationMessageAsync(MsgTypePrintJobResponse, response);
    public Task SendPrintJobStatusAsync(PrintJobStatus status) => SendApplicationMessageAsync(MsgTypePrintJobStatus, status);

    private async Task<IPEndPoint?> ResolveEndpointAsync(DeviceInfo device)
    {
        var candidates = device.NatCandidates?
            .Where(candidate =>
                candidate.Port > 0 &&
                !string.IsNullOrWhiteSpace(candidate.IPAddress) &&
                string.Equals(candidate.Protocol, "udp", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? new List<NatEndpointCandidate>();

        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(device.PublicIPAddress) && device.PublicPort is > 0)
        {
            candidates.Add(new NatEndpointCandidate
            {
                IPAddress = device.PublicIPAddress,
                Port = device.PublicPort.Value,
                Type = NatCandidateType.ServerReflexive,
                Priority = 100
            });
        }

        if (candidates.Count > 0)
        {
            var result = await _natTraversalService.TryConnectAsync(candidates);
            if (result.Success && !string.IsNullOrWhiteSpace(result.RemoteIPAddress) && result.RemotePort is > 0)
                return new IPEndPoint(IPAddress.Parse(result.RemoteIPAddress), result.RemotePort.Value);
        }

        if (!string.IsNullOrWhiteSpace(device.IPAddress) && device.Port > 0 && IPAddress.TryParse(device.IPAddress, out var address))
            return new IPEndPoint(address, device.Port);

        return null;
    }

    private async Task SendApplicationMessageAsync<T>(string messageType, T payload)
    {
        await SendMessageAsync(new NetworkMessage
        {
            MessageType = messageType,
            Payload = Encode(payload)
        });
    }

    private async Task SendControlMessageAsync<T>(string messageType, T payload)
    {
        await SendMessageAsync(new NetworkMessage
        {
            MessageType = messageType,
            Payload = Encode(payload)
        });
    }

    private async Task SendMessageAsync(NetworkMessage message)
    {
        if (_remoteEndpoint is null)
            return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        var fragmentCount = (int)Math.Ceiling(bytes.Length / (double)MaxFragmentPayloadBytes);
        var messageId = Guid.NewGuid();

        for (var index = 0; index < fragmentCount; index++)
        {
            var offset = index * MaxFragmentPayloadBytes;
            var count = Math.Min(MaxFragmentPayloadBytes, bytes.Length - offset);
            var datagram = new byte[HeaderSize + count];

            Buffer.BlockCopy(Magic, 0, datagram, 0, Magic.Length);
            datagram[4] = 1;
            Buffer.BlockCopy(messageId.ToByteArray(), 0, datagram, 5, 16);
            Buffer.BlockCopy(BitConverter.GetBytes(fragmentCount), 0, datagram, 21, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(index), 0, datagram, 25, 4);
            Buffer.BlockCopy(bytes, offset, datagram, HeaderSize, count);

            await _natTraversalService.SendDatagramAsync(_remoteEndpoint.Address.ToString(), _remoteEndpoint.Port, datagram);

            if (fragmentCount > 1 && index < fragmentCount - 1)
                await Task.Delay(1);
        }
    }

    private void OnDatagramReceived(object? sender, NatDatagramReceivedEventArgs e)
    {
        if (e.Payload.Length < HeaderSize)
            return;

        if (!e.Payload.AsSpan(0, 4).SequenceEqual(Magic))
            return;

        var messageId = new Guid(e.Payload.AsSpan(5, 16));
        var fragmentCount = BitConverter.ToInt32(e.Payload, 21);
        var fragmentIndex = BitConverter.ToInt32(e.Payload, 25);
        if (fragmentCount <= 0 || fragmentIndex < 0 || fragmentIndex >= fragmentCount)
            return;

        var accumulator = _incomingFragments.GetOrAdd(messageId, _ => new FragmentAccumulator
        {
            FragmentCount = fragmentCount,
            Fragments = new byte[fragmentCount][]
        });

        if (accumulator.Fragments[fragmentIndex] is null)
        {
            accumulator.Fragments[fragmentIndex] = e.Payload.AsSpan(HeaderSize).ToArray();
            accumulator.ReceivedCount++;
        }

        if (accumulator.ReceivedCount != accumulator.FragmentCount)
            return;

        _incomingFragments.TryRemove(messageId, out _);

        var fullPayload = new byte[accumulator.Fragments.Sum(fragment => fragment.Length)];
        var offset = 0;
        foreach (var fragment in accumulator.Fragments)
        {
            Buffer.BlockCopy(fragment, 0, fullPayload, offset, fragment.Length);
            offset += fragment.Length;
        }

        DispatchMessage(e.RemoteIPAddress, e.RemotePort, fullPayload);
    }

    private void DispatchMessage(string remoteIPAddress, int remotePort, byte[] payload)
    {
        try
        {
            var message = JsonSerializer.Deserialize<NetworkMessage>(payload);
            if (message is null)
                return;

            if (message.MessageType == MsgTypeHello)
            {
                var previous = _remoteEndpoint;
                _remoteEndpoint = new IPEndPoint(IPAddress.Parse(remoteIPAddress), remotePort);
                _ = SendControlMessageAsync(MsgTypeHelloAck, string.Empty);
                if (previous is null || !previous.Equals(_remoteEndpoint))
                    ConnectionStateChanged?.Invoke(this, true);
                return;
            }

            if (message.MessageType == MsgTypeHelloAck)
            {
                _helloAckTcs?.TrySetResult(true);
                return;
            }

            if (_remoteEndpoint is null ||
                !_remoteEndpoint.Address.ToString().Equals(remoteIPAddress, StringComparison.OrdinalIgnoreCase) ||
                _remoteEndpoint.Port != remotePort)
            {
                return;
            }

            if (message.MessageType == MsgTypeDisconnect)
            {
                _remoteEndpoint = null;
                ConnectionStateChanged?.Invoke(this, false);
                return;
            }

            switch (message.MessageType)
            {
                case MsgTypeScreen:
                    InvokeDecoded(message.Payload, ScreenDataReceived);
                    break;
                case MsgTypeInput:
                    InvokeDecoded(message.Payload, InputEventReceived);
                    break;
                case MsgTypePairingRequest:
                    InvokeDecoded(message.Payload, PairingRequestReceived);
                    break;
                case MsgTypePairingResponse:
                    InvokeDecoded(message.Payload, PairingResponseReceived);
                    break;
                case MsgTypeConnectionQuality:
                    InvokeDecoded(message.Payload, ConnectionQualityReceived);
                    break;
                case MsgTypeSessionControlRequest:
                    InvokeDecoded(message.Payload, SessionControlRequestReceived);
                    break;
                case MsgTypeSessionControlResponse:
                    InvokeDecoded(message.Payload, SessionControlResponseReceived);
                    break;
                case MsgTypeClipboard:
                    InvokeDecoded(message.Payload, ClipboardDataReceived);
                    break;
                case MsgTypeFileTransferRequest:
                    InvokeDecoded(message.Payload, FileTransferRequestReceived);
                    break;
                case MsgTypeFileTransferResponse:
                    InvokeDecoded(message.Payload, FileTransferResponseReceived);
                    break;
                case MsgTypeFileTransferChunk:
                    InvokeDecoded(message.Payload, FileTransferChunkReceived);
                    break;
                case MsgTypeFileTransferComplete:
                    InvokeDecoded(message.Payload, FileTransferCompleteReceived);
                    break;
                case MsgTypeAudio:
                    InvokeDecoded(message.Payload, AudioDataReceived);
                    break;
                case MsgTypeChatMessage:
                    InvokeDecoded(message.Payload, ChatMessageReceived);
                    break;
                case MsgTypeMessageRead:
                    InvokeDecoded(message.Payload, MessageReadReceived);
                    break;
                case MsgTypePrintJob:
                    InvokeDecoded(message.Payload, PrintJobReceived);
                    break;
                case MsgTypePrintJobResponse:
                    InvokeDecoded(message.Payload, PrintJobResponseReceived);
                    break;
                case MsgTypePrintJobStatus:
                    InvokeDecoded(message.Payload, PrintJobStatusReceived);
                    break;
            }
        }
        catch
        {
        }
    }

    private static void InvokeDecoded<T>(string payload, EventHandler<T>? handler)
    {
        if (handler is null)
            return;

        var decoded = Decode<T>(payload);
        if (decoded is not null)
            handler.Invoke(null, decoded);
    }

    private static string Encode<T>(T value)
        => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value));

    private static T? Decode<T>(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(Convert.FromBase64String(payload));
        }
        catch
        {
            return default;
        }
    }

    private void CleanupFragments()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        foreach (var fragment in _incomingFragments)
        {
            if (fragment.Value.CreatedAtUtc < cutoff)
                _incomingFragments.TryRemove(fragment.Key, out _);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UdpNatCommunicationService));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cleanupTimer.Dispose();
        if (_started)
            _natTraversalService.DatagramReceived -= OnDatagramReceived;
        _connectLock.Dispose();
    }
}
