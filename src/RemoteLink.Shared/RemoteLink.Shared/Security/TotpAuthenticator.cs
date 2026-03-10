using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RemoteLink.Shared.Security;

/// <summary>
/// Generates and validates RFC 6238 TOTP codes for authenticator-app based MFA.
/// </summary>
public static class TotpAuthenticator
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int DefaultSecretLengthBytes = 20;
    public const int DefaultDigits = 6;
    public const int DefaultPeriodSeconds = 30;

    public static string GenerateSecretKey(int byteLength = DefaultSecretLengthBytes)
    {
        if (byteLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength), "Secret length must be positive.");

        return Base32Encode(RandomNumberGenerator.GetBytes(byteLength));
    }

    public static string BuildProvisioningUri(string issuer, string accountName, string secretKey, int digits = DefaultDigits, int periodSeconds = DefaultPeriodSeconds)
    {
        if (string.IsNullOrWhiteSpace(issuer))
            throw new ArgumentException("Issuer is required.", nameof(issuer));

        if (string.IsNullOrWhiteSpace(accountName))
            throw new ArgumentException("Account name is required.", nameof(accountName));

        ValidateTotpSettings(digits, periodSeconds);

        var normalizedIssuer = issuer.Trim();
        var normalizedAccountName = accountName.Trim();
        var normalizedSecret = NormalizeSecret(secretKey);
        var label = $"{normalizedIssuer}:{normalizedAccountName}";

        return FormattableString.Invariant(
            $"otpauth://totp/{Uri.EscapeDataString(label)}?secret={normalizedSecret}&issuer={Uri.EscapeDataString(normalizedIssuer)}&algorithm=SHA1&digits={digits}&period={periodSeconds}");
    }

    public static string GenerateCode(string secretKey, DateTimeOffset? timestamp = null, int digits = DefaultDigits, int periodSeconds = DefaultPeriodSeconds)
    {
        ValidateTotpSettings(digits, periodSeconds);

        var secretBytes = DecodeBase32(NormalizeSecret(secretKey));
        var currentTimestamp = timestamp ?? DateTimeOffset.UtcNow;
        var counter = currentTimestamp.ToUnixTimeSeconds() / periodSeconds;
        Span<byte> counterBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var truncatedHash = ((hash[offset] & 0x7F) << 24) |
                            (hash[offset + 1] << 16) |
                            (hash[offset + 2] << 8) |
                            hash[offset + 3];

        var divisor = 1;
        for (var index = 0; index < digits; index++)
            divisor *= 10;

        var code = truncatedHash % divisor;
        return code.ToString(new string('0', digits), CultureInfo.InvariantCulture);
    }

    public static bool VerifyCode(string secretKey, string code, DateTimeOffset? timestamp = null, int allowedTimeDriftWindows = 1, int digits = DefaultDigits, int periodSeconds = DefaultPeriodSeconds)
    {
        if (allowedTimeDriftWindows < 0)
            throw new ArgumentOutOfRangeException(nameof(allowedTimeDriftWindows), "Allowed time drift windows must be zero or greater.");

        ValidateTotpSettings(digits, periodSeconds);
        var normalizedCode = NormalizeCode(code, digits);
        if (normalizedCode is null)
            return false;

        var currentTimestamp = timestamp ?? DateTimeOffset.UtcNow;
        var expectedBytes = Encoding.ASCII.GetBytes(normalizedCode);

        for (var offset = -allowedTimeDriftWindows; offset <= allowedTimeDriftWindows; offset++)
        {
            var candidate = GenerateCode(secretKey, currentTimestamp.AddSeconds(offset * periodSeconds), digits, periodSeconds);
            if (CryptographicOperations.FixedTimeEquals(expectedBytes, Encoding.ASCII.GetBytes(candidate)))
                return true;
        }

        return false;
    }

    private static void ValidateTotpSettings(int digits, int periodSeconds)
    {
        if (digits <= 0 || digits > 9)
            throw new ArgumentOutOfRangeException(nameof(digits), "Digits must be between 1 and 9.");

        if (periodSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodSeconds), "Period must be positive.");
    }

    private static string NormalizeSecret(string secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new ArgumentException("Secret key is required.", nameof(secretKey));

        var normalized = new string(secretKey
            .Where(character => !char.IsWhiteSpace(character) && character != '-')
            .Select(char.ToUpperInvariant)
            .ToArray());

        if (normalized.Length == 0)
            throw new ArgumentException("Secret key is required.", nameof(secretKey));

        if (normalized.Any(character => !Base32Alphabet.Contains(character)))
            throw new ArgumentException("Secret key contains invalid Base32 characters.", nameof(secretKey));

        return normalized;
    }

    private static string? NormalizeCode(string code, int digits)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var normalized = new string(code.Where(char.IsDigit).ToArray());
        return normalized.Length == digits ? normalized : null;
    }

    private static string Base32Encode(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        var output = new StringBuilder((int)Math.Ceiling(bytes.Length / 5d) * 8);
        var bitBuffer = 0;
        var bitsInBuffer = 0;

        foreach (var currentByte in bytes)
        {
            bitBuffer = (bitBuffer << 8) | currentByte;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                var index = (bitBuffer >> bitsInBuffer) & 0x1F;
                output.Append(Base32Alphabet[index]);
            }

            bitBuffer &= (1 << bitsInBuffer) - 1;
        }

        if (bitsInBuffer > 0)
        {
            var index = (bitBuffer << (5 - bitsInBuffer)) & 0x1F;
            output.Append(Base32Alphabet[index]);
        }

        return output.ToString();
    }

    private static byte[] DecodeBase32(string base32)
    {
        var output = new List<byte>(base32.Length * 5 / 8);
        var bitBuffer = 0;
        var bitsInBuffer = 0;

        foreach (var character in base32)
        {
            var value = Base32Alphabet.IndexOf(character);
            if (value < 0)
                throw new ArgumentException("Secret key contains invalid Base32 characters.", nameof(base32));

            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                output.Add((byte)((bitBuffer >> bitsInBuffer) & 0xFF));
                bitBuffer &= (1 << bitsInBuffer) - 1;
            }
        }

        return output.ToArray();
    }
}
