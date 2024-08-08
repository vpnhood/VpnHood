using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;

namespace VpnHood.AccessServer.Providers.Acme;

public class AcmeOrderProvider(
    IOrderContext orderContext,
    IChallengeContext challengeContext,
    CertificateSigningRequest csr)
    : IAcmeOrderProvider
{
    public string KeyAuthorization => challengeContext.KeyAuthz;
    public string Token => challengeContext.Token;

    public async Task<X509Certificate2?> Validate()
    {
        var res = await challengeContext.Validate();
        return res.Status switch {
            ChallengeStatus.Pending => null,
            ChallengeStatus.Processing => null,
            ChallengeStatus.Valid => await GenerateCertificate(orderContext, csr),
            _ => throw new Exception("Challenge failed.")
        };
    }

    private static async Task<X509Certificate2> GenerateCertificate(IOrderContext order, CertificateSigningRequest csr)
    {
        var privateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
        var cert = await order.Generate(new CsrInfo {
            CommonName = csr.CommonName,
            Organization = csr.Organization,
            OrganizationUnit = csr.OrganizationUnit,
            CountryName = csr.LocationCountry,
            State = csr.LocationState,
            Locality = csr.LocationCity
        }, privateKey);


        // Export PFX
        var pfxBuilder = cert.ToPfx(privateKey);
        var pfx = pfxBuilder.Build(csr.CommonName, "");
        var certificate = new X509Certificate2(pfx, "", X509KeyStorageFlags.Exportable);
        return certificate;
    }
}