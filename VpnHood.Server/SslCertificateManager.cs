using System;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    public class SslCertificateManager
    {
        private readonly string _serverId;
        private readonly IAccessServer _accessServer;
        private readonly ConcurrentDictionary<IPAddress, X509Certificate2> _certificates = new ConcurrentDictionary<IPAddress, X509Certificate2>();

        public SslCertificateManager(string serverId, IAccessServer accessServer)
        {
            _serverId = serverId;
            _accessServer = accessServer;
        }

        public async Task<X509Certificate2> GetCertificate(IPAddress ipAddress)
        {
            // find in cache and return if not expired
            if (_certificates.TryGetValue(ipAddress, out X509Certificate2 certificate) && certificate.NotAfter > DateTime.Now)
                return certificate;

            // get from access server
            var certificateData = await _accessServer.GetSslCertificateData(_serverId, ipAddress.ToString());
            certificate = new X509Certificate2(certificateData);
            _certificates.TryAdd(ipAddress, certificate);
            return certificate;
        }
    }
}