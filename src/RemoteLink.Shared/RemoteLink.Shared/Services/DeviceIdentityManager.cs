using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Manages stable local device identities and formatting for internet-facing numeric IDs.
/// </summary>
public static class DeviceIdentityManager
{
    private sealed class PersistedDeviceIdentity
    {
        public string DeviceId { get; set; } = string.Empty;
    }

    public static DeviceInfo CreateOrLoadLocalDevice(string profileName, string deviceName, DeviceType type, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);

        return new DeviceInfo
        {
            DeviceId = GetOrCreateStableDeviceId(profileName),
            DeviceName = deviceName,
            Type = type,
            Port = port
        };
    }

    public static string GetOrCreateStableDeviceId(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        var path = GetIdentityPath(profileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            if (File.Exists(path))
            {
                var existing = JsonSerializer.Deserialize<PersistedDeviceIdentity>(File.ReadAllText(path));
                if (!string.IsNullOrWhiteSpace(existing?.DeviceId))
                    return existing.DeviceId;
            }
        }
        catch
        {
        }

        var created = new PersistedDeviceIdentity
        {
            DeviceId = $"{SanitizeProfileName(profileName)}_{Guid.NewGuid():N}"
        };

        File.WriteAllText(path, JsonSerializer.Serialize(created, new JsonSerializerOptions { WriteIndented = true }));
        return created.DeviceId;
    }

    public static string GetPreferredDisplayId(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var internetId = FormatInternetDeviceId(device.InternetDeviceId);
        return !string.IsNullOrWhiteSpace(internetId)
            ? internetId
            : GenerateLegacyNumericId(device.DeviceName);
    }

    public static string GenerateLegacyNumericId(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes((seed ?? string.Empty) + "RemoteLink"));
        var value = Math.Abs(BitConverter.ToInt64(hash, 0));
        var digits = ((value % 900_000_000) + 100_000_000).ToString("000000000");
        return FormatInternetDeviceId(digits);
    }

    public static string? NormalizeInternetDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 9 ? digits : null;
    }

    public static string FormatInternetDeviceId(string? value)
    {
        var normalized = NormalizeInternetDeviceId(value);
        if (normalized is null)
            return string.Empty;

        return $"{normalized[..3]} {normalized[3..6]} {normalized[6..]}";
    }

    private static string GetIdentityPath(string profileName)
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "RemoteLink", "identities", $"{SanitizeProfileName(profileName)}.json");
    }

    private static string SanitizeProfileName(string profileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(profileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "device" : sanitized;
    }
}
