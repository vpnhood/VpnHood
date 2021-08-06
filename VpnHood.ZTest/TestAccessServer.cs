using VpnHood.Server;
using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;
using VpnHood.Server.Exceptions;

namespace VpnHood.Test
{
    public class TestAccessServer : IAccessServer
    {
        private readonly IAccessServer _accessServer;
        public IPEndPoint RedirectServerEndPoint { get; set; }

        public bool IsMaintenanceMode { get; set; } = false;

        public TestAccessServer(IAccessServer accessServer)
        {
            _accessServer = accessServer;
        }

        private void VerifyMaintananceMode()
        {
            if (IsMaintenanceMode)
                throw new MaintenanceException();
        }

        public Task<byte[]> GetSslCertificateData(string serverEndPoint)
        {
            VerifyMaintananceMode();
            return _accessServer.GetSslCertificateData(serverEndPoint);
        }
        public Task SendServerStatus(ServerStatus serverStatus)
        {
            VerifyMaintananceMode();
            return _accessServer.SendServerStatus(serverStatus);
        }

        public Task SubscribeServer(ServerInfo serverInfo)
        {
            VerifyMaintananceMode();
            return _accessServer.SubscribeServer(serverInfo);
        }

        public Task<Access> AddUsage(string accessId, UsageInfo usageInfo)
        {
            VerifyMaintananceMode();
            return _accessServer.AddUsage(accessId, usageInfo);
        }

        public async Task<Access> GetAccess(AccessRequest accessRequest)
        {
            VerifyMaintananceMode();

            var res = await _accessServer.GetAccess(accessRequest);
            if (RedirectServerEndPoint != null && !accessRequest.RequestEndPoint.Equals(RedirectServerEndPoint))
            {
                res.RedirectServerEndPoint = RedirectServerEndPoint;
                res.StatusCode = AccessStatusCode.RedirectServer;
            }
            return res;
        }
    }
}
