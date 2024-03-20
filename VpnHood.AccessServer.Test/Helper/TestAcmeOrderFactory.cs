using VpnHood.AccessServer.Dtos.Certificates;
using VpnHood.AccessServer.Services.Acme;

namespace VpnHood.AccessServer.Test.Helper;

public class TestAcmeOrderFactory : IAcmeOrderFactory
{
    public Task<string> CreateNewAccount(string email)
    {
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task<IAcmeOrderService> CreateOrder(string accountPem, CertificateSigningRequest csr)
    {
        var orderService = new TestAcmeOrderService(csr);
        return Task.FromResult((IAcmeOrderService)orderService);
    }
}