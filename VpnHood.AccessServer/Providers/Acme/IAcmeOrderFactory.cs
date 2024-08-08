namespace VpnHood.AccessServer.Providers.Acme;

public interface IAcmeOrderFactory
{
    Task<string> CreateNewAccount(string email);
    Task<IAcmeOrderProvider> CreateOrder(string accountPem, CertificateSigningRequest csr);
}