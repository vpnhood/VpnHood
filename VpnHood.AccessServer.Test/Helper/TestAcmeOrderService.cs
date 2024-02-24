using System.Security.Cryptography.X509Certificates;
using VpnHood.AccessServer.Dtos.Certificates;
using VpnHood.AccessServer.Services.Acme;
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Test.Helper;

public class TestAcmeOrderService(CertificateSigningRequest csr)
    : IAcmeOrderService
{
    public const string TestKeyAuthorization = "test.KeyAuthorization";
    public const string TestToken = "test.Token";

    public string KeyAuthorization => TestKeyAuthorization;
    public string Token => TestToken;

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