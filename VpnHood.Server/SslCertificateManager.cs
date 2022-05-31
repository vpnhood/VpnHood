using System;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.Common.Exceptions;

namespace VpnHood.Server;

public class SslCertificateManager
{
    private readonly IAccessServer _accessServer;
    private readonly ConcurrentDictionary<IPEndPoint, X509Certificate2> _certificates = new();
    private readonly Lazy<X509Certificate2> _maintenanceCertificate = new(InitMaintenanceCertificate);

    public SslCertificateManager(IAccessServer accessServer)
    {
        _accessServer = accessServer;
    }

    private static X509Certificate2 InitMaintenanceCertificate()
    {
        var subjectName = $"CN={CertificateUtil.CreateRandomDns()}, OU=MT";
        using var cert = CertificateUtil.CreateSelfSigned(subjectName);

        // it is required to set X509KeyStorageFlags
        var ret = new X509Certificate2(cert.Export(X509ContentType.Pfx), "", X509KeyStorageFlags.Exportable);
        return ret;
    }

    public async Task<X509Certificate2> GetCertificate(IPEndPoint ipEndPoint)
    {
        // find in cache
        if (_certificates.TryGetValue(ipEndPoint, out var certificate))
            return certificate;

        // get from access server
        try
        {
            var certificateData = await _accessServer.GetSslCertificateData(ipEndPoint);
            certificate = new X509Certificate2(certificateData);
            _certificates.TryAdd(ipEndPoint, certificate);
            return certificate;
        }
        catch (MaintenanceException)
        {
            return _maintenanceCertificate.Value;
        }
    }

    public void ClearCache()
    {
        foreach (var item in _certificates.Values)
            item.Dispose();
        _certificates.Clear();
    }
}