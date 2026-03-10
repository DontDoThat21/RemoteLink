using RemoteLink.Shared.Security;
using Xunit;

namespace RemoteLink.Shared.Tests.Security;

public sealed class TotpAuthenticatorTests
{
    [Fact]
    public void GenerateSecretKey_ReturnsBase32Value()
    {
        var secret = TotpAuthenticator.GenerateSecretKey();

        Assert.False(string.IsNullOrWhiteSpace(secret));
        Assert.All(secret, character => Assert.Contains(character, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
    }

    [Fact]
    public void GenerateCode_AndVerifyCode_RoundTrip()
    {
        var secret = TotpAuthenticator.GenerateSecretKey();
        var timestamp = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var code = TotpAuthenticator.GenerateCode(secret, timestamp);

        Assert.True(TotpAuthenticator.VerifyCode(secret, code, timestamp));
        Assert.False(TotpAuthenticator.VerifyCode(secret, "000000", timestamp));
    }

    [Fact]
    public void BuildProvisioningUri_IncludesIssuerAndAccountName()
    {
        var uri = TotpAuthenticator.BuildProvisioningUri("RemoteLink", "alice@example.com", TotpAuthenticator.GenerateSecretKey());

        Assert.StartsWith("otpauth://totp/", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=RemoteLink", uri, StringComparison.Ordinal);
        Assert.Contains("alice%40example.com", uri, StringComparison.Ordinal);
    }
}
