using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Security;

namespace RemoteLink.Shared.Services;

public sealed class PresentationSessionClient : IAsyncDisposable
{
    private sealed class PresentationEnvelope
    {
        public string MessageType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }

    private const string MessageTypeJoinRequest = "PresentationJoinRequest";
    private const string MessageTypeJoinResponse = "PresentationJoinResponse";
    private const string MessageTypeScreenData = "ScreenData";
    private const string MessageTypeConnectionQuality = "ConnectionQuality";
    private const string MessageTypeAnnotation = "PresentationAnnotation";

    private readonly TlsConfiguration _tlsConfiguration;
    private TcpClient? _client;
    private Stream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoopTask;

    public PresentationSessionClient(TlsConfiguration? tlsConfiguration = null)
    {
        _tlsConfiguration = tlsConfiguration ?? TlsConfiguration.CreateDefault();
    }

    public bool IsConnected => _client?.Connected == true && _stream is not null;

    public event EventHandler<ScreenData>? ScreenDataReceived;
    public event EventHandler<ConnectionQuality>? ConnectionQualityReceived;
    public event EventHandler<PresentationAnnotationMessage>? AnnotationReceived;
    public event EventHandler<bool>? ConnectionStateChanged;

    public async Task SendAnnotationAsync(PresentationAnnotationMessage annotationMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(annotationMessage);

        if (_stream is null)
            throw new InvalidOperationException("The presentation client is not connected.");

        var effectiveToken = cancellationToken;
        if (!effectiveToken.CanBeCanceled && _cts is not null)
            effectiveToken = _cts.Token;

        await SendEnvelopeAsync(new PresentationEnvelope
        {
            MessageType = MessageTypeAnnotation,
            Payload = Encode(annotationMessage)
        }, effectiveToken).ConfigureAwait(false);
    }

    public async Task<PresentationJoinResponse> ConnectAsync(DeviceInfo host, string pin, string viewerDeviceId, string viewerDeviceName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        await DisconnectAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(host.IPAddress))
        {
            return new PresentationJoinResponse
            {
                Success = false,
                Message = "The host does not have a reachable IP address."
            };
        }

        int port = host.PresentationPort is > 0 ? host.PresentationPort.Value : PresentationSessionHost.DefaultPort;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var client = new TcpClient();
        await client.ConnectAsync(host.IPAddress, port, _cts.Token).ConfigureAwait(false);
        _client = client;

        Stream stream = client.GetStream();
        if (_tlsConfiguration.Enabled)
        {
            var sslStream = new SslStream(stream, false, ValidateServerCertificate);
            await sslStream.AuthenticateAsClientAsync(
                _tlsConfiguration.TargetHost ?? host.IPAddress,
                clientCertificates: null,
                enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
                checkCertificateRevocation: false).ConfigureAwait(false);
            stream = sslStream;
        }

        _stream = stream;

        var request = new PresentationJoinRequest
        {
            ViewerDeviceId = viewerDeviceId,
            ViewerDeviceName = viewerDeviceName,
            Pin = pin,
            RequestedAt = DateTime.UtcNow
        };

        await SendEnvelopeAsync(new PresentationEnvelope
        {
            MessageType = MessageTypeJoinRequest,
            Payload = Encode(request)
        }, _cts.Token).ConfigureAwait(false);

        var responseEnvelope = await ReadEnvelopeAsync(stream, _cts.Token).ConfigureAwait(false);
        var response = responseEnvelope is not null && string.Equals(responseEnvelope.MessageType, MessageTypeJoinResponse, StringComparison.Ordinal)
            ? Decode<PresentationJoinResponse>(responseEnvelope.Payload)
            : null;

        if (response?.Success != true)
        {
            await DisconnectAsync().ConfigureAwait(false);
            return response ?? new PresentationJoinResponse { Success = false, Message = "Presentation join failed." };
        }

        ConnectionStateChanged?.Invoke(this, true);
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        return response;
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();

        var stream = Interlocked.Exchange(ref _stream, null);
        if (stream is not null)
        {
            try
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        var client = Interlocked.Exchange(ref _client, null);
        try
        {
            client?.Dispose();
        }
        catch
        {
        }

        if (_receiveLoopTask is not null)
        {
            try
            {
                await _receiveLoopTask.ConfigureAwait(false);
            }
            catch
            {
            }

            _receiveLoopTask = null;
        }

        _cts?.Dispose();
        _cts = null;
        ConnectionStateChanged?.Invoke(this, false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                var envelope = await ReadEnvelopeAsync(_stream, cancellationToken).ConfigureAwait(false);
                if (envelope is null)
                    break;

                switch (envelope.MessageType)
                {
                    case MessageTypeScreenData:
                        var screenData = Decode<ScreenData>(envelope.Payload);
                        if (screenData is not null)
                            ScreenDataReceived?.Invoke(this, screenData);
                        break;

                    case MessageTypeConnectionQuality:
                        var quality = Decode<ConnectionQuality>(envelope.Payload);
                        if (quality is not null)
                            ConnectionQualityReceived?.Invoke(this, quality);
                        break;

                    case MessageTypeAnnotation:
                        var annotation = Decode<PresentationAnnotationMessage>(envelope.Payload);
                        if (annotation is not null)
                            AnnotationReceived?.Invoke(this, annotation);
                        break;
                }
            }
        }
        catch
        {
        }
        finally
        {
            ConnectionStateChanged?.Invoke(this, false);
        }
    }

    private async Task SendEnvelopeAsync(PresentationEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_stream is null)
            return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var length = BitConverter.GetBytes(payload.Length);
        await _stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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

    private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (_tlsConfiguration.ValidateRemoteCertificate)
            return sslPolicyErrors == SslPolicyErrors.None;

        return sslPolicyErrors == SslPolicyErrors.None || sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}
