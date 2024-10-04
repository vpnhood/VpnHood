using VpnHood.AccessServer.Providers.Acme;

namespace VpnHood.AccessServer.Test.Helper;

public class TestAcmeOrderFactory : IAcmeOrderFactory
{
    public Task<string> CreateNewAccount(string email)
    {
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task<IAcmeOrderProvider> CreateOrder(string accountPem, CertificateSigningRequest csr)
    {
        var orderService = new TestAcmeOrderProvider(csr);
        return Task.FromResult((IAcmeOrderProvider)orderService);
    }
}