using VpnHood.AccessServer.Dtos.Certificates;

namespace VpnHood.AccessServer.Services.Acme;

public interface IAcmeOrderFactory
{
    Task<string> CreateNewAccount(string email);
    Task<IAcmeOrderService> CreateOrder(string accountPem, CertificateSigningRequest csr);
}