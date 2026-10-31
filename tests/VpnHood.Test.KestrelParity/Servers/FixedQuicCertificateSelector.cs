using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Server.Listeners;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class FixedQuicCertificateSelector(X509Certificate certificate) : IQuicCertificateSelector
{
    public X509Certificate SelectCertificate(string? hostName) => certificate;
}
