using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteLink.Shared.Security;

/// <summary>
/// Manages TLS certificates and security settings for encrypted communication.
/// </summary>
public class TlsConfiguration
{
    /// <summary>
    /// Whether TLS encryption is enabled. When <c>false</c>, plain TCP is used.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Server certificate for the desktop host (server mode).
    /// If <c>null</c>, a self-signed certificate will be generated on first use.
    /// </summary>
    public X509Certificate2? ServerCertificate { get; set; }

    /// <summary>
    /// Whether to validate the remote certificate. For local network use,
    /// this can be set to <c>false</c> to allow self-signed certificates.
    /// Default: <c>false</c> (trust self-signed certs for LAN).
    /// </summary>
    public bool ValidateRemoteCertificate { get; set; } = false;

    /// <summary>
    /// Target host name for certificate validation (client mode).
    /// If <c>null</c>, certificate CN/SAN validation is skipped.
    /// </summary>
    public string? TargetHost { get; set; }

    /// <summary>
    /// Creates a default <see cref="TlsConfiguration"/> for local network use:
    /// TLS enabled, self-signed certificates accepted, no hostname validation.
    /// </summary>
    public static TlsConfiguration CreateDefault() => new()
    {
        Enabled = true,
        ValidateRemoteCertificate = false,
        TargetHost = null
    };

    /// <summary>
    /// Generates a self-signed X.509 certificate valid for 1 year.
    /// Used by the desktop host when no certificate is explicitly provided.
    /// </summary>
    /// <param name="subjectName">Certificate subject (e.g., "CN=RemoteLink Host").</param>
    /// <returns>A new <see cref="X509Certificate2"/> with private key.</returns>
    public static X509Certificate2 GenerateSelfSignedCertificate(string subjectName = "CN=RemoteLink")
    {
        using var rsa = RSA.Create(2048);
        
        var request = new CertificateRequest(
            subjectName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Mark as valid for server authentication
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // serverAuth
                critical: true));

        // Self-sign with 1-year validity
        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // Export and re-import to ensure the private key is properly attached on all platforms
        var pfxBytes = cert.Export(X509ContentType.Pfx, string.Empty);
        return new X509Certificate2(pfxBytes, string.Empty, X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// Saves a certificate to a PFX file (PKCS#12).
    /// </summary>
    /// <param name="certificate">Certificate with private key.</param>
    /// <param name="path">File path to save to.</param>
    /// <param name="password">Optional password to protect the private key.</param>
    public static void SaveCertificate(X509Certificate2 certificate, string path, string? password = null)
    {
        var pfxBytes = certificate.Export(X509ContentType.Pfx, password ?? string.Empty);
        File.WriteAllBytes(path, pfxBytes);
    }

    /// <summary>
    /// Loads a certificate from a PFX file (PKCS#12).
    /// </summary>
    /// <param name="path">Path to the PFX file.</param>
    /// <param name="password">Password for the private key.</param>
    /// <returns>The loaded certificate with private key.</returns>
    public static X509Certificate2 LoadCertificate(string path, string? password = null)
    {
        return new X509Certificate2(path, password, X509KeyStorageFlags.Exportable);
    }
}
