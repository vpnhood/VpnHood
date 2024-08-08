using Certes;
using Certes.Acme;

namespace VpnHood.AccessServer.Providers.Acme;

public class AcmeOrderFactory : IAcmeOrderFactory
{
    public async Task<string> CreateNewAccount(string email)
    {
        var acmeContext = new AcmeContext(WellKnownServers.LetsEncryptV2);
        var accountContext = await acmeContext.NewAccount(null, true);
        await acmeContext.Authorization(accountContext.Location).Challenges();
        var pemKey = acmeContext.AccountKey.ToPem();
        return pemKey;
    }

    public async Task<IAcmeOrderProvider> CreateOrder(string accountPem, CertificateSigningRequest csr)
    {
        var accountKey = KeyFactory.FromPem(accountPem);
        var acmeContext = new AcmeContext(WellKnownServers.LetsEncryptV2, accountKey);
        var orderContext = await acmeContext.NewOrder([csr.CommonName]);

        var authorizations = await orderContext.Authorizations();
        var challenge = await authorizations.Single().Http();
        return new AcmeOrderProvider(orderContext, challenge, csr);
    }
}