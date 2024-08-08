using System.Security.Cryptography.X509Certificates;

namespace VpnHood.AccessServer.Providers.Acme;

public interface IAcmeOrderProvider
{
    string KeyAuthorization { get; }
    string Token { get; }
    Task<X509Certificate2?> Validate();
}