using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemoteLink.Shared.Models;

/// <summary>
/// Secure tunnel operating mode.
/// </summary>
public enum SecureTunnelMode
{
    Disabled,
    Preferred,
    Required
}

/// <summary>
/// Configures the optional secure tunnel transport used to route all session traffic through an encrypted relay tunnel.
/// </summary>
public sealed class SecureTunnelConfiguration
{
    public SecureTunnelMode Mode { get; set; }
    public string SharedSecret { get; set; } = string.Empty;

    public bool IsConfigured =>
        Mode != SecureTunnelMode.Disabled &&
        !string.IsNullOrWhiteSpace(SharedSecret);

    public bool RequiresTunnel => Mode == SecureTunnelMode.Required;

    public void ApplyTo(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        device.SupportsSecureTunnel = IsConfigured;
        device.RequiresSecureTunnel = IsConfigured && RequiresTunnel;
    }

    public static SecureTunnelConfiguration FromEnvironment(
        string secretEnvironmentVariable = "REMOTELINK_TUNNEL_SECRET",
        string modeEnvironmentVariable = "REMOTELINK_TUNNEL_MODE")
    {
        var secret = Environment.GetEnvironmentVariable(secretEnvironmentVariable)?.Trim();
        var modeText = Environment.GetEnvironmentVariable(modeEnvironmentVariable)?.Trim();

        var mode = modeText?.ToLowerInvariant() switch
        {
            "preferred" or "on" or "enabled" or "true" => SecureTunnelMode.Preferred,
            "required" or "strict" or "force" => SecureTunnelMode.Required,
            _ => SecureTunnelMode.Disabled
        };

        return new SecureTunnelConfiguration
        {
            Mode = mode,
            SharedSecret = secret ?? string.Empty
        };
    }
}

internal sealed class SecureTunnelEnvelope
{
    public string Algorithm { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string Ciphertext { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
}

internal static class SecureTunnelPayloadCodec
{
    private const string Algorithm = "AES-256-GCM";

    public static string Encode(
        byte[] payload,
        string sharedSecret,
        string sessionId,
        string localDeviceId,
        string remoteDeviceId)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var key = DeriveKey(sharedSecret, sessionId, localDeviceId, remoteDeviceId);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key);
        aes.Encrypt(nonce, payload, ciphertext, tag, BuildAssociatedData(sessionId, localDeviceId, remoteDeviceId));

        var envelope = new SecureTunnelEnvelope
        {
            Algorithm = Algorithm,
            SessionId = sessionId,
            Nonce = Convert.ToBase64String(nonce),
            Ciphertext = Convert.ToBase64String(ciphertext),
            Tag = Convert.ToBase64String(tag)
        };

        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(envelope));
    }

    public static bool TryDecode(
        string payload,
        string sharedSecret,
        string sessionId,
        string localDeviceId,
        string remoteDeviceId,
        out byte[] plaintext)
    {
        plaintext = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            var envelope = JsonSerializer.Deserialize<SecureTunnelEnvelope>(Convert.FromBase64String(payload));
            if (envelope is null ||
                !string.Equals(envelope.Algorithm, Algorithm, StringComparison.Ordinal) ||
                !string.Equals(envelope.SessionId, sessionId, StringComparison.Ordinal))
            {
                return false;
            }

            var nonce = Convert.FromBase64String(envelope.Nonce);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            var tag = Convert.FromBase64String(envelope.Tag);
            plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(DeriveKey(sharedSecret, sessionId, localDeviceId, remoteDeviceId));
            aes.Decrypt(nonce, ciphertext, tag, plaintext, BuildAssociatedData(sessionId, localDeviceId, remoteDeviceId));
            return true;
        }
        catch
        {
            plaintext = Array.Empty<byte>();
            return false;
        }
    }

    private static byte[] DeriveKey(string sharedSecret, string sessionId, string localDeviceId, string remoteDeviceId)
    {
        var (deviceA, deviceB) = SortDeviceIds(localDeviceId, remoteDeviceId);
        return SHA256.HashData(Encoding.UTF8.GetBytes($"{sharedSecret}\n{sessionId}\n{deviceA}\n{deviceB}"));
    }

    private static byte[] BuildAssociatedData(string sessionId, string localDeviceId, string remoteDeviceId)
    {
        var (deviceA, deviceB) = SortDeviceIds(localDeviceId, remoteDeviceId);
        return Encoding.UTF8.GetBytes($"RemoteLinkSecureTunnel|{sessionId}|{deviceA}|{deviceB}");
    }

    private static (string DeviceA, string DeviceB) SortDeviceIds(string localDeviceId, string remoteDeviceId)
    {
        return string.CompareOrdinal(localDeviceId, remoteDeviceId) <= 0
            ? (localDeviceId, remoteDeviceId)
            : (remoteDeviceId, localDeviceId);
    }
}
