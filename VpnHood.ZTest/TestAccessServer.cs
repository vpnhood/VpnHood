using VpnHood.Server;
using System.Threading.Tasks;
using System;
using VpnHood.Server.AccessServers;

#nullable enable
namespace VpnHood.Test
{
    public class TestAccessServer : IAccessServer
    {
        private readonly RestAccessServer _restAccessServer;
        public TestEmbedIoAccessServer EmbedIoAccessServer { get; }
        public IAccessServer BaseAccessServer { get; }

        public TestAccessServer(IAccessServer baseAccessServer)
        {
            BaseAccessServer = baseAccessServer;
            EmbedIoAccessServer = new TestEmbedIoAccessServer(baseAccessServer);
            _restAccessServer = new RestAccessServer(EmbedIoAccessServer.BaseUri, "Bearer", Guid.Empty);
        }

        public bool IsMaintenanceMode => _restAccessServer.IsMaintenanceMode;
        public Task<byte[]> GetSslCertificateData(string hostEndPoint) => _restAccessServer.GetSslCertificateData(hostEndPoint);
        public Task SendServerStatus(ServerStatus serverStatus) => _restAccessServer.SendServerStatus(serverStatus);
        public Task SubscribeServer(ServerInfo serverInfo) => _restAccessServer.SubscribeServer(serverInfo);
        public Task<Access> AddUsage(string accessId, UsageInfo usageInfo) => _restAccessServer.AddUsage(accessId, usageInfo);
        public Task<Access> GetAccess(AccessRequest accessRequest) => _restAccessServer.GetAccess(accessRequest);

        public void Dispose()
        {
            _restAccessServer.Dispose();
            EmbedIoAccessServer.Dispose();
            BaseAccessServer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
