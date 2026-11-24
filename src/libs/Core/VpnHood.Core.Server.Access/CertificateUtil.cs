using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.Access;

/// <summary>
/// Provides the making and reading of TLS certificates for a VPN server.
/// </summary>
public static class CertificateUtil
{
    // pronounceable, as a string of random letters looks like a malware domain
    private static string GenerateName(int syllableCount)
    {
        string[] onsets = [
            "b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v", "w", "z",
            "br", "cl", "dr", "fl", "gr", "pl", "sh", "st", "tr"
        ];
        string[] vowels = ["a", "e", "i", "o", "u", "a", "e", "i", "o", "ai", "ea", "ou"];
        string[] codas = ["", "", "", "", "", "", "", "", "l", "m", "n", "r", "s", "t", "x", "nd"];

        var random = Random.Shared;
        var name = "";
        for (var i = 0; i < syllableCount; i++)
            name += onsets[random.Next(onsets.Length)] + vowels[random.Next(vowels.Length)] +
                    codas[random.Next(codas.Length)];

        return name;
    }

    /// <summary>
    /// Makes up a host name: a host of the kind many sites keep for an API or their files, a pronounceable name of three
    /// or four syllables, and <c>.com</c>, <c>.net</c> or <c>.org</c>, meant to have no fixed shape a network could match.
    /// </summary>
    /// <returns>The host name.</returns>
    public static string CreateRandomDns()
    {
        string[] hosts = ["api", "app", "assets", "cdn", "data", "edge", "files", "img", "media", "static"];
        string[] extensions = [".com", ".net", ".org"];

        var random = Random.Shared;
        return hosts[random.Next(hosts.Length)] + "." + GenerateName(random.Next(3, 5)) +
               extensions[random.Next(extensions.Length)];
    }

    /// <summary>Gets the certificate a domain presents in its TLS handshake, without checking it.</summary>
    /// <param name="domain">The domain, which is also the name the handshake asks for.</param>
    /// <param name="port">The port. The default is 443.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the certificate.</returns>
    /// <remarks>The connection's and the handshake's own errors pass through, a cancel's too.</remarks>
    /// <exception cref="ArgumentException">The domain is empty.</exception>
    /// <exception cref="InvalidOperationException">The domain presented no certificate.</exception>
    public static async Task<X509Certificate2> GetCertificateFromDomain(string domain, int port = 443,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(domain, port, cancellationToken).Vhc();

        // the certificate is what is asked for, whoever signed it
        await using var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, _, _, _) => true);

        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = domain }, cancellationToken).Vhc();

        // a copy, as the stream's goes with it
        return sslStream.RemoteCertificate is { } certificate
            ? new X509Certificate2(certificate)
            : throw new InvalidOperationException("The domain presented no certificate.");
    }

    /// <summary>Makes a copy of a certificate whose private key is kept and can be exported.</summary>
    /// <param name="certificate">The certificate, with its private key.</param>
    /// <param name="password">The password the copy goes through PKCS#12 with. The default is empty.</param>
    /// <returns>The copy.</returns>
    public static X509Certificate2 CreateExportable(X509Certificate2 certificate, string password = "")
    {
        // Export with the Exportable flag to ensure the key is usable
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx, password), password,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    }

    /// <summary>Makes a self-signed certificate, SHA-256 with a new RSA key, valid from now.</summary>
    /// <param name="subjectName">
    /// The subject, or <see langword="null"/> for <c>CN=</c> and a made-up host name (<see cref="CreateRandomDns"/>).
    /// </param>
    /// <param name="notAfter">The end of its validity, or <see langword="null"/> for 20 years from now.</param>
    /// <returns>The certificate, with its private key, which can be exported.</returns>
    public static X509Certificate2 CreateSelfSigned(string? subjectName = null, DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create();
        return CreateSelfSigned(rsa, subjectName, notAfter);
    }

    /// <summary>
    /// Makes a self-signed copy of a certificate's subject and extensions, SHA-256 with a new RSA key.
    /// </summary>
    /// <param name="originalCert">The certificate to copy.</param>
    /// <param name="noBefore">The start of its validity, or <see langword="null"/> for the original's.</param>
    /// <param name="notAfter">The end of its validity, or <see langword="null"/> for the original's.</param>
    /// <returns>The certificate, with its private key, which can be exported.</returns>
    /// <remarks>On Windows, it takes the original's friendly name too.</remarks>
    public static X509Certificate2 CreateSelfSigned(X509Certificate2 originalCert, DateTime? noBefore = null,
        DateTime? notAfter = null)
    {
        using var rsa = RSA.Create();
        var request = new CertificateRequest(originalCert.SubjectName, rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Copy all extensions from the original certificate
        foreach (var extension in originalCert.Extensions)
            request.CertificateExtensions.Add(extension);

        // Use the same validity period as the original certificate
        noBefore ??= originalCert.NotBefore;
        notAfter ??= originalCert.NotAfter;

        var selfSignedCert = request.CreateSelfSigned(noBefore.Value, notAfter.Value);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            selfSignedCert.FriendlyName = originalCert.FriendlyName;
        return CreateExportable(selfSignedCert);
    }

    /// <summary>Makes a self-signed certificate, SHA-256 with a given RSA key, valid from now.</summary>
    /// <param name="rsa">The key.</param>
    /// <param name="subjectName">
    /// The subject, or <see langword="null"/> for <c>CN=</c> and a made-up host name (<see cref="CreateRandomDns"/>).
    /// </param>
    /// <param name="notAfter">The end of its validity, or <see langword="null"/> for 20 years from now.</param>
    /// <returns>The certificate, with its private key, which can be exported.</returns>
    public static X509Certificate2 CreateSelfSigned(RSA rsa, string? subjectName = null,
        DateTimeOffset? notAfter = null)
    {
        subjectName ??= $"CN={CreateRandomDns()}";
        notAfter ??= DateTimeOffset.UtcNow.AddYears(20);

        // Create fake authority Root
        var certRequest = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var rootCertificate = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow, notAfter.Value);
        return CreateExportable(rootCertificate);
    }

    /// <summary>
    /// Makes a certificate with a given RSA key under a made-up chain of two authorities that bear DigiCert's names,
    /// valid from now.
    /// </summary>
    /// <param name="rsa">The key.</param>
    /// <param name="subjectName">
    /// The subject, or <see langword="null"/> for <c>CN=</c> and a made-up host name (<see cref="CreateRandomDns"/>).
    /// </param>
    /// <param name="notAfter">The end of its validity, or <see langword="null"/> for 5 years from now.</param>
    /// <returns>
    /// The certificate alone, with its private key, which can be exported; the made-up authorities are not part of it.
    /// </returns>
    /// <remarks>A made-up authority cannot sign as the real one, so the chain passes no check of its signatures.</remarks>
    public static X509Certificate2 CreateChained(RSA rsa, string? subjectName = null,
        DateTimeOffset? notAfter = null)
    {
        subjectName ??= $"CN={CreateRandomDns()}";
        var notBefore = DateTimeOffset.UtcNow;
        notAfter ??= notBefore.AddYears(5);

        // the authorities' dates cover the certificate's, as an issuer must outlast what it signs
        using var rootRsa = RSA.Create();
        using var rootCertificate = CreateAuthorityRequest(
                "CN = DigiCert Global Root CA, OU = www.digicert.com, O = DigiCert Inc, C = US", rootRsa)
            .CreateSelfSigned(notBefore.AddYears(-10), notAfter.Value.AddYears(10));

        using var authorityRsa = RSA.Create();
        using var authorityPublicCertificate = CreateAuthorityRequest(
                "CN = DigiCert SHA2 Secure Server CA, O = DigiCert Inc, C = US", authorityRsa)
            .Create(rootCertificate, notBefore.AddYears(-5), notAfter.Value.AddYears(5), CreateSerialNumber());

        using var authorityCertificate = authorityPublicCertificate.CopyWithPrivateKey(authorityRsa);
        using var publicCertificate =
            new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .Create(authorityCertificate, notBefore, notAfter.Value, CreateSerialNumber());

        using var certificate = publicCertificate.CopyWithPrivateKey(rsa);
        return CreateExportable(certificate);
    }

    private static CertificateRequest CreateAuthorityRequest(string subjectName, RSA rsa)
    {
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature, true));

        return request;
    }

    private static byte[] CreateSerialNumber()
    {
        var serialNumber = new byte[20];
        Random.Shared.NextBytes(serialNumber);
        return serialNumber;
    }
}