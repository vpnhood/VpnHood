using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Core.Server.Listeners;

internal interface IQuicCertificateSelector
{
    X509Certificate SelectCertificate(string? hostName);
}
