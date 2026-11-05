using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.Access;

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

    // a made-up name in no fixed shape for a network to match: of any length, under a common ending, and at times on a
    // host of the kind many sites keep for an API or their files. Long enough to be a real site's name only rarely
    public static string CreateRandomDns()
    {
        string[] hosts = ["api", "app", "assets", "cdn", "data", "edge", "files", "img", "media", "static"];
        string[] extensions = [".com", ".net", ".org", ".io", ".co", ".info"];

        var random = Random.Shared;
        var name = GenerateName(random.Next(3, 5)) + extensions[random.Next(extensions.Length)];
        return random.Next(2) == 0 ? name : $"{hosts[random.Next(hosts.Length)]}.{name}";
    }

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

    public static X509Certificate2 CreateExportable(X509Certificate2 certificate, string password = "")
    {
        // Export with the Exportable flag to ensure the key is usable
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx, password), password,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    }

    public static X509Certificate2 CreateSelfSigned(string? subjectName = null, DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create();
        return CreateSelfSigned(rsa, subjectName, notAfter);
    }

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