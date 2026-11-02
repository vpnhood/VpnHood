using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Core.Server;

// what a handshake needs of a certificate, made with the configuration: its host name, and its chain, built as
// Kestrel builds it; given the certificate alone, SslStream builds the chain again on every handshake (about 2 ms of
// CPU for a Let's Encrypt ECDSA chain)
public class CertificateHostName(X509Certificate2 certificate)
{
    public string HostName { get; } = certificate.GetNameInfo(X509NameType.DnsName, false) ??
                                      throw new Exception("Could not get the HostName from the certificate.");
    public X509Certificate2 Certificate { get; } = certificate;
    public SslStreamCertificateContext CertificateContext { get; } =
        SslStreamCertificateContext.Create(certificate, additionalCertificates: null);
}