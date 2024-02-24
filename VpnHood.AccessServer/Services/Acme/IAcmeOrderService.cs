using System.Security.Cryptography.X509Certificates;

namespace VpnHood.AccessServer.Services.Acme;

public interface IAcmeOrderService
{
    string KeyAuthorization { get; }
    string Token { get; }
    Task<X509Certificate2?> Validate();
}