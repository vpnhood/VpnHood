using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Core.Server;

// cache HostName for performance
public class CertificateHostName(X509Certificate2 certificate)
{
    public string HostName { get; } = certificate.GetNameInfo(X509NameType.DnsName, false) ??
                                      throw new Exception("Could not get the HostName from the certificate.");
    public X509Certificate2 Certificate { get; } = certificate;
}