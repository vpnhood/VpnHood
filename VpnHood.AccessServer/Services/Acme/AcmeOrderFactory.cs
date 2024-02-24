using Certes;
using Certes.Acme;
using VpnHood.AccessServer.Dtos.Certificates;

namespace VpnHood.AccessServer.Services.Acme;

public class AcmeOrderFactory
{
    public async Task<string> CreateNewAccount(string email)
    {
        var acmeContext = new AcmeContext(WellKnownServers.LetsEncryptStagingV2);
        var accountContext = await acmeContext.NewAccount(null, true);
        await acmeContext.Authorization(accountContext.Location).Challenges();
        var pemKey = acmeContext.AccountKey.ToPem();
        return pemKey;
    }

    public async Task<AcmeOrderService> CreateOrder(string accountPem, CertificateSigningRequest csr)
    {
        var accountKey = KeyFactory.FromPem(accountPem);
        var acmeContext = new AcmeContext(WellKnownServers.LetsEncryptStagingV2, accountKey);
        var orderContext = await acmeContext.NewOrder([csr.CommonName]);

        var authorizations = await orderContext.Authorizations();
        var challenge = await authorizations.Single().Http();
        return new AcmeOrderService(orderContext, challenge, csr);
    }
}