using System;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    public class SslCertificateManager
    {
        private readonly IAccessServer _accessServer;
        private readonly Guid _serverId;
        private readonly ConcurrentDictionary<IPEndPoint, X509Certificate2> _certificates = new();

        public SslCertificateManager(IAccessServer accessServer, Guid serverId)
        {
            _accessServer = accessServer;
            _serverId = serverId;
        }

        public async Task<X509Certificate2> GetCertificate(IPEndPoint ipEndPoint)
        {
            // find in cache and return if not expired
            if (_certificates.TryGetValue(ipEndPoint, out var certificate) && certificate.NotAfter > DateTime.Now)
                return certificate;

            // get from access server
            var certificateData = await _accessServer.GetSslCertificateData(_serverId, ipEndPoint.ToString());
            certificate = new X509Certificate2(certificateData);
            _certificates.TryAdd(ipEndPoint, certificate);
            return certificate;
        }
    }
}