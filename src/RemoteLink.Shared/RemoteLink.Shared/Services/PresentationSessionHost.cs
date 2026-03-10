using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Security;

namespace RemoteLink.Shared.Services;

public sealed class PresentationSessionHost : IAsyncDisposable
{
    private sealed class PresentationEnvelope
    {
        public string MessageType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }

    private sealed class ViewerConnection : IAsyncDisposable
    {
        public required string ViewerId { get; init; }
        public required TcpClient Client { get; init; }
        public required Stream Stream { get; init; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);

        public async Task SendAsync(PresentationEnvelope envelope, CancellationToken cancellationToken = default)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
            var length = BitConverter.GetBytes(payload.Length);

            await WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await Stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
                await Stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                WriteLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Stream.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                Client.Dispose();
            }
            catch
            {
            }

            WriteLock.Dispose();
        }
    }

    private const string MessageTypeJoinRequest = "PresentationJoinRequest";
    private const string MessageTypeJoinResponse = "PresentationJoinResponse";
    private const string MessageTypeScreenData = "ScreenData";
    private const string MessageTypeConnectionQuality = "ConnectionQuality";
    private const string MessageTypeAnnotation = "PresentationAnnotation";

    private readonly ConcurrentDictionary<string, ViewerConnection> _viewers = new(StringComparer.OrdinalIgnoreCase);
    private readonly PresentationAnnotationBoard _annotationBoard = new();
    private readonly TlsConfiguration _tlsConfiguration;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private string _currentPin = string.Empty;
    private bool _isPresentationActive;

    public PresentationSessionHost(TlsConfiguration? tlsConfiguration = null)
    {
        _tlsConfiguration = tlsConfiguration ?? TlsConfiguration.CreateDefault();
    }

    public const int DefaultPort = 12348;

    public bool IsRunning => _listener is not null;
    public bool IsPresentationActive => _isPresentationActive;
    public int ViewerCount => _viewers.Count;
    public int Port { get; private set; } = DefaultPort;
    public string? SessionName { get; private set; }

    public IReadOnlyList<PresentationAnnotation> GetAnnotations() => _annotationBoard.GetAnnotations();

    public event EventHandler? SessionStateChanged;
    public event EventHandler<int>? ViewerCountChanged;

    public Task StartAsync(int port = DefaultPort, CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
            return Task.CompletedTask;

        Port = port;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public Task ActivateAsync(string pin, string? sessionName = null)
    {
        _currentPin = pin ?? string.Empty;
        SessionName = string.IsNullOrWhiteSpace(sessionName) ? "Presentation" : sessionName.Trim();
        _isPresentationActive = true;
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task DeactivateAsync(CancellationToken cancellationToken = default)
    {
        _isPresentationActive = false;
        _currentPin = string.Empty;
        SessionName = null;
        SessionStateChanged?.Invoke(this, EventArgs.Empty);

        var viewers = _viewers.Keys.ToList();
        foreach (var viewerId in viewers)
            await RemoveViewerAsync(viewerId).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<PresentationBroadcastResult> BroadcastScreenDataAsync(ScreenData screenData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screenData);
        return await BroadcastAsync(MessageTypeScreenData, screenData, screenData.ImageData?.LongLength ?? 0, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PresentationBroadcastResult> BroadcastConnectionQualityAsync(ConnectionQuality quality, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quality);
        return await BroadcastAsync(MessageTypeConnectionQuality, quality, 0, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PresentationBroadcastResult> BroadcastAnnotationAsync(PresentationAnnotationMessage annotationMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(annotationMessage);
        return await ApplyAnnotationMessageAsync(annotationMessage, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();

        var listeners = Interlocked.Exchange(ref _listener, null);
        try
        {
            listeners?.Stop();
        }
        catch
        {
        }

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch
            {
            }

            _acceptLoopTask = null;
        }

        await DeactivateAsync().ConfigureAwait(false);
        _cts?.Dispose();
        _cts = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleViewerAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleViewerAsync(TcpClient client, CancellationToken cancellationToken)
    {
        ViewerConnection? viewer = null;
        try
        {
            Stream stream = client.GetStream();
            if (_tlsConfiguration.Enabled)
            {
                var certificate = _tlsConfiguration.ServerCertificate
                    ?? TlsConfiguration.GenerateSelfSignedCertificate("CN=RemoteLink Presentation Host");
                var sslStream = new SslStream(stream, false, ValidateClientCertificate);
                await sslStream.AuthenticateAsServerAsync(
                    certificate,
                    clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
                    checkCertificateRevocation: false).ConfigureAwait(false);
                stream = sslStream;
            }

            var requestEnvelope = await ReadEnvelopeAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(requestEnvelope?.MessageType, MessageTypeJoinRequest, StringComparison.Ordinal))
                return;

            var request = Decode<PresentationJoinRequest>(requestEnvelope.Payload);
            if (request is null)
                return;

            var response = BuildJoinResponse(request);
            await SendEnvelopeAsync(stream, new PresentationEnvelope
            {
                MessageType = MessageTypeJoinResponse,
                Payload = Encode(response)
            }, cancellationToken).ConfigureAwait(false);

            if (!response.Success)
                return;

            viewer = new ViewerConnection
            {
                ViewerId = string.IsNullOrWhiteSpace(request.ViewerDeviceId) ? Guid.NewGuid().ToString("N") : request.ViewerDeviceId,
                Client = client,
                Stream = stream
            };

            _viewers[viewer.ViewerId] = viewer;
            ViewerCountChanged?.Invoke(this, ViewerCount);
            SessionStateChanged?.Invoke(this, EventArgs.Empty);

            await SendCurrentAnnotationsAsync(viewer, cancellationToken).ConfigureAwait(false);
            await WaitForViewerDisconnectAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            if (viewer is not null)
                await RemoveViewerAsync(viewer.ViewerId).ConfigureAwait(false);
            else
                client.Dispose();
        }
    }

    private PresentationJoinResponse BuildJoinResponse(PresentationJoinRequest request)
    {
        if (!_isPresentationActive)
        {
            return new PresentationJoinResponse
            {
                Success = false,
                Message = "Presentation mode is not active on the host."
            };
        }

        if (string.IsNullOrWhiteSpace(_currentPin) || !string.Equals(request.Pin, _currentPin, StringComparison.Ordinal))
        {
            return new PresentationJoinResponse
            {
                Success = false,
                Message = "Invalid presentation PIN."
            };
        }

        return new PresentationJoinResponse
        {
            Success = true,
            Message = "Presentation joined.",
            SessionId = Guid.NewGuid().ToString(),
            SessionName = SessionName,
            SessionPermissions = SessionPermissionSet.CreateViewOnly()
        };
    }

    private async Task WaitForViewerDisconnectAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var envelope = await ReadEnvelopeAsync(stream, cancellationToken).ConfigureAwait(false);
            if (envelope is null)
                break;

            if (string.Equals(envelope.MessageType, MessageTypeAnnotation, StringComparison.Ordinal))
            {
                var annotationMessage = Decode<PresentationAnnotationMessage>(envelope.Payload);
                if (annotationMessage is not null)
                    await ApplyAnnotationMessageAsync(annotationMessage, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<PresentationBroadcastResult> ApplyAnnotationMessageAsync(PresentationAnnotationMessage annotationMessage, CancellationToken cancellationToken)
    {
        var appliedMessage = _annotationBoard.Apply(annotationMessage);
        long payloadBytes = JsonSerializer.SerializeToUtf8Bytes(appliedMessage).LongLength;
        return await BroadcastAsync(MessageTypeAnnotation, appliedMessage, payloadBytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendCurrentAnnotationsAsync(ViewerConnection viewer, CancellationToken cancellationToken)
    {
        foreach (var annotation in _annotationBoard.GetAnnotations())
        {
            var message = new PresentationAnnotationMessage
            {
                Action = PresentationAnnotationAction.Upsert,
                AnnotationId = annotation.AnnotationId,
                Annotation = annotation,
                ChangedByDeviceId = annotation.CreatedByDeviceId,
                ChangedAtUtc = annotation.CreatedAtUtc
            };

            await SendAnnotationAsync(viewer, message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<PresentationBroadcastResult> BroadcastAsync(string messageType, object payload, long payloadBytes, CancellationToken cancellationToken)
    {
        if (!_isPresentationActive || _viewers.IsEmpty)
            return new PresentationBroadcastResult();

        var envelope = new PresentationEnvelope
        {
            MessageType = messageType,
            Payload = Encode(payload)
        };

        var result = new PresentationBroadcastResult();
        foreach (var viewer in _viewers.Values.ToList())
        {
            try
            {
                var startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await viewer.SendAsync(envelope, cancellationToken).ConfigureAwait(false);
                var latency = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedAt;
                result.SuccessfulViewerCount++;
                result.TotalBytesSent += payloadBytes;
                result.MaxSendLatencyMs = Math.Max(result.MaxSendLatencyMs, latency);
            }
            catch
            {
                await RemoveViewerAsync(viewer.ViewerId).ConfigureAwait(false);
            }
        }

        return result;
    }

    private static Task SendAnnotationAsync(ViewerConnection viewer, PresentationAnnotationMessage annotationMessage, CancellationToken cancellationToken)
    {
        return viewer.SendAsync(new PresentationEnvelope
        {
            MessageType = MessageTypeAnnotation,
            Payload = Encode(annotationMessage)
        }, cancellationToken);
    }

    private async Task RemoveViewerAsync(string viewerId)
    {
        if (_viewers.TryRemove(viewerId, out var viewer))
        {
            await viewer.DisposeAsync().ConfigureAwait(false);
            ViewerCountChanged?.Invoke(this, ViewerCount);
            SessionStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static async Task SendEnvelopeAsync(Stream stream, PresentationEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var length = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PresentationEnvelope?> ReadEnvelopeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];
        var read = await ReadExactAsync(stream, lengthBuffer, 0, 4, cancellationToken).ConfigureAwait(false);
        if (read < 4)
            return null;

        int length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length <= 0)
            return null;

        var payload = new byte[length];
        read = await ReadExactAsync(stream, payload, 0, length, cancellationToken).ConfigureAwait(false);
        if (read < length)
            return null;

        return JsonSerializer.Deserialize<PresentationEnvelope>(payload);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            totalRead += read;
        }

        return totalRead;
    }

    private static string Encode<T>(T payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Convert.ToBase64String(json);
    }

    private static T? Decode<T>(string payload)
    {
        var json = Convert.FromBase64String(payload);
        return JsonSerializer.Deserialize<T>(json);
    }

    private static bool ValidateClientCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        => true;

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
