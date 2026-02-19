using RemoteLink.Shared.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteLink.Shared.Tests;

public class TlsConfigurationTests
{
    [Fact]
    public void CreateDefault_ShouldReturnEnabledConfiguration()
    {
        // Act
        var config = TlsConfiguration.CreateDefault();

        // Assert
        Assert.True(config.Enabled);
        Assert.False(config.ValidateRemoteCertificate);
        Assert.Null(config.TargetHost);
        Assert.Null(config.ServerCertificate);
    }

    [Fact]
    public void GenerateSelfSignedCertificate_ShouldCreateValidCertificate()
    {
        // Act
        var cert = TlsConfiguration.GenerateSelfSignedCertificate("CN=Test");

        // Assert
        Assert.NotNull(cert);
        Assert.True(cert.HasPrivateKey);
        Assert.Contains("CN=Test", cert.Subject);
        Assert.True(cert.NotBefore <= DateTime.UtcNow);
        Assert.True(cert.NotAfter > DateTime.UtcNow);
    }

    [Fact]
    public void GenerateSelfSignedCertificate_DefaultSubject_ShouldUseRemoteLink()
    {
        // Act
        var cert = TlsConfiguration.GenerateSelfSignedCertificate();

        // Assert
        Assert.NotNull(cert);
        Assert.Contains("CN=RemoteLink", cert.Subject);
    }

    [Fact]
    public void GenerateSelfSignedCertificate_ShouldBeExportable()
    {
        // Act
        var cert = TlsConfiguration.GenerateSelfSignedCertificate();

        // Assert - should not throw
        var pfx = cert.Export(X509ContentType.Pfx);
        Assert.NotNull(pfx);
        Assert.True(pfx.Length > 0);
    }

    [Fact]
    public void SaveAndLoadCertificate_ShouldRoundTrip()
    {
        // Arrange
        var originalCert = TlsConfiguration.GenerateSelfSignedCertificate("CN=RoundTrip");
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-cert-{Guid.NewGuid()}.pfx");

        try
        {
            // Act - Save
            TlsConfiguration.SaveCertificate(originalCert, tempPath);
            Assert.True(File.Exists(tempPath));

            // Act - Load
            var loadedCert = TlsConfiguration.LoadCertificate(tempPath);

            // Assert
            Assert.NotNull(loadedCert);
            Assert.Equal(originalCert.Subject, loadedCert.Subject);
            Assert.Equal(originalCert.Thumbprint, loadedCert.Thumbprint);
            Assert.True(loadedCert.HasPrivateKey);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void SaveAndLoadCertificate_WithPassword_ShouldRoundTrip()
    {
        // Arrange
        var originalCert = TlsConfiguration.GenerateSelfSignedCertificate("CN=Password");
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-cert-pwd-{Guid.NewGuid()}.pfx");
        const string password = "SecureP@ssw0rd";

        try
        {
            // Act - Save
            TlsConfiguration.SaveCertificate(originalCert, tempPath, password);

            // Act - Load
            var loadedCert = TlsConfiguration.LoadCertificate(tempPath, password);

            // Assert
            Assert.NotNull(loadedCert);
            Assert.Equal(originalCert.Thumbprint, loadedCert.Thumbprint);
            Assert.True(loadedCert.HasPrivateKey);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void LoadCertificate_WithWrongPassword_ShouldThrow()
    {
        // Arrange
        var cert = TlsConfiguration.GenerateSelfSignedCertificate();
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-cert-badpwd-{Guid.NewGuid()}.pfx");

        try
        {
            TlsConfiguration.SaveCertificate(cert, tempPath, "correct");

            // Act & Assert
            Assert.Throws<CryptographicException>(() => 
                TlsConfiguration.LoadCertificate(tempPath, "wrong"));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void TlsConfiguration_Properties_ShouldBeSettable()
    {
        // Arrange
        var cert = TlsConfiguration.GenerateSelfSignedCertificate();
        var config = new TlsConfiguration();

        // Act
        config.Enabled = false;
        config.ValidateRemoteCertificate = true;
        config.TargetHost = "example.com";
        config.ServerCertificate = cert;

        // Assert
        Assert.False(config.Enabled);
        Assert.True(config.ValidateRemoteCertificate);
        Assert.Equal("example.com", config.TargetHost);
        Assert.Same(cert, config.ServerCertificate);
    }

    [Fact]
    public void GenerateSelfSignedCertificate_ShouldHaveServerAuthUsage()
    {
        // Act
        var cert = TlsConfiguration.GenerateSelfSignedCertificate();

        // Assert - Check for enhanced key usage extension
        var ekuExtension = cert.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();

        Assert.NotNull(ekuExtension);
        
        // Server authentication OID is 1.3.6.1.5.5.7.3.1
        var hasServerAuth = ekuExtension.EnhancedKeyUsages
            .Cast<System.Security.Cryptography.Oid>()
            .Any(oid => oid.Value == "1.3.6.1.5.5.7.3.1");

        Assert.True(hasServerAuth, "Certificate should have Server Authentication extended key usage");
    }

    [Fact]
    public void GenerateSelfSignedCertificate_ValidityPeriod_ShouldBeOneYear()
    {
        // Act
        var cert = TlsConfiguration.GenerateSelfSignedCertificate();

        // Assert
        var validityDays = (cert.NotAfter - cert.NotBefore).TotalDays;
        
        // Should be approximately 365 days (allow 2-day tolerance for the -1 day start offset)
        Assert.InRange(validityDays, 363, 367);
    }
}
