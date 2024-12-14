using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Core.Server.Access;

public static class CertificateUtil
{
    private static string GenerateName(int length, bool pascalCase = false)
    {
        var random = new Random();
        string[] consonants = [
            "b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "q", "r", "s", "sh", "zh", "t", "v", "w",
            "x"
        ];
        string[] vowels = ["a", "e", "i", "o", "u", "ae", "y"];
        var name = "";
        for (var i = 0; i < length; i += 2) {
            name += consonants[random.Next(consonants.Length)];
            name += vowels[random.Next(vowels.Length)];
        }

        if (pascalCase)
            name = name[0].ToString().ToUpper() + name[1..];

        return name;
    }

    public static string CreateRandomDns()
    {
        var extensions = new[] { ".com", ".net", ".org" };

        var random = new Random();
        var ret = GenerateName(random.Next(2, 3)) + "." + GenerateName(random.Next(7, 10));
        ret += extensions[random.Next(0, 2)];
        return ret;
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
            using var client = new HttpClient(handler);
            await client.GetAsync(url, cancellationToken);
        }
        catch (Exception) {
            // ignore
        }
        return serverCertificate ?? throw new Exception("Could not extract certificate from url");
    }

    public static X509Certificate2 CreateExportable(X509Certificate2 certificate, string password = "")
    {
        // Export with the Exportable flag to ensure the key is usable
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx, password), password,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    }

    public static X509Certificate2 CreateSelfSigned(string? subjectName = null, DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create();
        return CreateSelfSigned(rsa, subjectName, notAfter);
    }

    public static X509Certificate2 CreateSelfSigned(X509Certificate2 originalCert, DateTime? noBefore = null, DateTime? notAfter = null)
    {
        using var rsa = RSA.Create();
        var request = new CertificateRequest(originalCert.SubjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

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

    public static X509Certificate2 CreateSelfSigned(RSA rsa, string? subjectName = null, DateTimeOffset? notAfter = null)
    {
        subjectName ??= $"CN={CreateRandomDns()}";
        notAfter ??= DateTimeOffset.Now.AddYears(20);

        // Create fake authority Root
        var certRequest = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var rootCertificate = certRequest.CreateSelfSigned(DateTimeOffset.Now, notAfter.Value);
        return CreateExportable(rootCertificate);
    }

    // ReSharper disable once UnusedMember.Global
    public static X509Certificate2 CreateChained(RSA rsa, string? subjectName = null,
        DateTimeOffset? notAfter = null)
    {
        subjectName ??= $"CN={CreateRandomDns()}";
        notAfter ??= DateTimeOffset.Now.AddYears(5);

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
            new DateTimeOffset(new DateTime(2006, 11, 9, 16, 0, 0)),
            new DateTimeOffset(new DateTime(2031, 11, 9, 16, 0, 0)));

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
            new DateTimeOffset(new DateTime(2013, 3, 8, 16, 0, 0)),
            new DateTimeOffset(new DateTime(2027, 3, 8, 16, 0, 0)),
            serial);
        authorityCertificate = authorityCertificate.CopyWithPrivateKey(rsa2);

        // Create certificate Root
        certRequest = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        serial = new byte[20];
        random.NextBytes(serial);
        var certificate = certRequest.Create(authorityCertificate, DateTimeOffset.Now, notAfter.Value, serial);
        certificate = certificate.CopyWithPrivateKey(rsa);

        return CreateExportable(certificate);
    }
}