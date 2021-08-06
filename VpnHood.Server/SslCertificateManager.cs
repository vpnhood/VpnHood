using System;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.Server.Exceptions;

namespace VpnHood.Server
{
    public class SslCertificateManager
    {
        private readonly IAccessServer _accessServer;
        private readonly ConcurrentDictionary<IPEndPoint, X509Certificate2> _certificates = new();
        private readonly Lazy<X509Certificate2> MaintenanceCertificate = new(InitMaintenanceCertificate);
        private DateTime LastMaintenanceTime = DateTime.Now;

        private static X509Certificate2 InitMaintenanceCertificate()
        {
            var subjectName = $"CN={CertificateUtil.CreateRandomDNS()}, OU=MT";
            using var cert =  CertificateUtil.CreateSelfSigned(subjectName);

            // it is required to set X509KeyStorageFlags
            var ret = new X509Certificate2(cert.Export(X509ContentType.Pfx), "", X509KeyStorageFlags.Exportable); 
            return ret;
        }

        public SslCertificateManager(IAccessServer accessServer)
        {
            _accessServer = accessServer;
        }

        public async Task<X509Certificate2> GetCertificate(IPEndPoint ipEndPoint)
        {
            // check maintenance mode
            if (_accessServer.IsMaintenanceMode && (DateTime.Now - LastMaintenanceTime).TotalMinutes < 1)
                return MaintenanceCertificate.Value;

            // find in cache 
            if (_certificates.TryGetValue(ipEndPoint, out var certificate))
                return certificate;

            // get from access server
            try
            {
                var certificateData = await _accessServer.GetSslCertificateData(ipEndPoint.ToString());
                certificate = new X509Certificate2(certificateData);
                _certificates.TryAdd(ipEndPoint, certificate);
                return certificate;
            }
            catch (MaintenanceException)
            {
                ClearCache();
                LastMaintenanceTime = DateTime.Now;
                return MaintenanceCertificate.Value;
            }
        }

        public void ClearCache()
        {
            foreach (var item in _certificates.Values)
                item.Dispose();
            _certificates.Clear();
        }
    }
}