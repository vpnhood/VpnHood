using System.Security.Cryptography.X509Certificates;
using VpnHood.AccessServer.Providers.Acme;
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Test.Helper;

public class TestAcmeOrderProvider(CertificateSigningRequest csr)
    : IAcmeOrderProvider
{
    public string KeyAuthorization { get; } = Guid.NewGuid().ToString();
    public string Token { get; } = Guid.NewGuid().ToString();

    public async Task<X509Certificate2?> Validate()
    {
        await ValidateByManager(csr.CommonName!, Token, KeyAuthorization);
        return GenerateCertificate(csr);
    }

    private static async Task ValidateByManager(string commonName, string token, string keyAuthorization)
    {
        var httpClient = new HttpClient();
        var url = $"http://{commonName}/.well-known/acme-challenge/{token}";
        var result = await httpClient.GetStringAsync(url);
        if (result != keyAuthorization)
            throw new Exception($"{commonName} or the farm servers have not been configured properly.");
    }

    private static X509Certificate2 GenerateCertificate(CertificateSigningRequest csr)
    {
        var x509Certificate2 = CertificateUtil.CreateSelfSigned(subjectName: csr.BuildSubjectName());
        return x509Certificate2;
    }
}