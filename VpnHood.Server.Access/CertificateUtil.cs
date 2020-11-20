using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Server
{
    public static class CertificateUtil
    {
        private static string GenerateName(int length, bool pascalCase = false)
        {
            var random = new Random();
            string[] consonants = { "b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "q", "r", "s", "sh", "zh", "t", "v", "w", "x" };
            string[] vowels = { "a", "e", "i", "o", "u", "ae", "y" };
            var name = "";
            for (int i = 0; i < length; i += 2)
            {
                name += consonants[random.Next(consonants.Length)];
                name += vowels[random.Next(vowels.Length)];
            }

            if (pascalCase)
                name = name[0].ToString().ToUpper() + name[1..];

            return name;
        }

        private static string CreateRandomDNS()
        {
            var extensions = new string[] { ".com", ".net", ".org" };

            var random = new Random();
            var ret = GenerateName(random.Next(7, 10));
            ret += extensions[random.Next(0, 2)];
            return ret;
        }

        public static X509Certificate2 CreateSelfSigned(string subjectName = null, DateTimeOffset? notAfter = null)
        {
            using var rsa = RSA.Create();
            return CreateSelfSigned(rsa: rsa, subjectName: subjectName, notAfter: notAfter);
        }

        public static X509Certificate2 CreateSelfSigned(RSA rsa, string subjectName = null, DateTimeOffset? notAfter = null)
        {
            if (subjectName == null) subjectName = $"CN={CreateRandomDNS()}";
            if (notAfter == null) notAfter = DateTimeOffset.Now.AddYears(20);

            // Create fake authority Root
            var certRequest = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var rootCertificate = certRequest.CreateSelfSigned(DateTimeOffset.Now, notAfter.Value);
            return rootCertificate;
        }

        public static X509Certificate2 CreateChained(RSA rsa, string subjectName = null, DateTimeOffset? notAfter = null)
        {
            if (subjectName == null) subjectName = $"CN={CreateRandomDNS()}";
            if (notAfter == null) notAfter = DateTimeOffset.Now.AddYears(5);

            var random = new Random();

            // Create fake authority Root
            using var rsa1 = RSA.Create();
            var certRequest = new CertificateRequest("CN = DigiCert Global Root CA, OU = www.digicert.com, O = DigiCert Inc, C = US", rsa1, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            // set basic certificate contraints
            certRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

            // key usage: Digital Signature and Key Encipherment
            certRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign |
                X509KeyUsageFlags.DigitalSignature
                , true));

            var rootCertificate = certRequest.CreateSelfSigned(
                new DateTimeOffset(new DateTime(2006, 11, 9, 16, 0, 0)),
                new DateTimeOffset(new DateTime(2031, 11, 9, 16, 0, 0)));

            // Create fake Root
            using var rsa2 = RSA.Create();
            certRequest = new CertificateRequest("CN = DigiCert SHA2 Secure Server CA, O = DigiCert Inc, C = US", rsa2, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            // set basic certificate contraints
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


            return certificate;
        }

    }
}