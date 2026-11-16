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

    /// <summary>Gets the certificate an HTTPS address presents, without checking it; the request's own errors are ignored.</summary>
    /// <param name="url">The address.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the certificate.</returns>
    /// <exception cref="Exception">The address presented no certificate.</exception>
    public static async Task<X509Certificate2> GetCertificateFromUrl(Uri url, CancellationToken cancellationToken)
    {
        X509Certificate2? serverCertificate = null;

        using var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) => {
            serverCertificate = cert != null ? new X509Certificate2(cert) : cert;
            return true; // Accept all certificates for this example
        };

        try {
            // ReSharper disable once ShortLivedHttpClient
            using var client = new HttpClient(handler);
            await client.GetAsync(url, cancellationToken).Vhc();
        }
        catch (Exception) {
            // ignore
        }

        return serverCertificate ?? throw new Exception("Could not extract certificate from url");
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
    /// <returns>The certificate, with its private key, which can be exported.</returns>
    /// <exception cref="ArgumentException">
    /// The end is past the made-up authority's, 8 March 2027, as the default's is now.
    /// </exception>
    // ReSharper disable once UnusedMember.Global
    public static X509Certificate2 CreateChained(RSA rsa, string? subjectName = null,
        DateTimeOffset? notAfter = null)
    {
        subjectName ??= $"CN={CreateRandomDns()}";
        notAfter ??= DateTimeOffset.UtcNow.AddYears(5);

        var random = new Random();

        // Create fake authority Root
        using var rsa1 = RSA.Create();
        var certRequest =
            new CertificateRequest("CN = DigiCert Global Root CA, OU = www.digicert.com, O = DigiCert Inc, C = US",
                rsa1, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // set basic certificate constraints
        certRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

        // key usage: Digital Signature and Key
        certRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign |
            X509KeyUsageFlags.DigitalSignature
            , true));

        var rootCertificate = certRequest.CreateSelfSigned(
            new DateTimeOffset(2006, 11, 9, 16, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2031, 11, 9, 16, 0, 0, TimeSpan.Zero));

        // Create fake Root
        using var rsa2 = RSA.Create();
        certRequest = new CertificateRequest("CN = DigiCert SHA2 Secure Server CA, O = DigiCert Inc, C = US", rsa2,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        // set basic certificate constraints
        certRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

        //// key usage: Digital Signature and Key Encipherment
        certRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign |
            X509KeyUsageFlags.DigitalSignature
            , true));

        var serial = new byte[20];
        random.NextBytes(serial);
        var authorityCertificate = certRequest.Create(rootCertificate,
            new DateTimeOffset(2013, 3, 8, 16, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 3, 8, 16, 0, 0, TimeSpan.Zero),
            serial);
        authorityCertificate = authorityCertificate.CopyWithPrivateKey(rsa2);

        // Create certificate Root
        certRequest = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        serial = new byte[20];
        random.NextBytes(serial);
        var certificate = certRequest.Create(authorityCertificate, DateTimeOffset.UtcNow, notAfter.Value, serial);
        certificate = certificate.CopyWithPrivateKey(rsa);

        return CreateExportable(certificate);
    }
}