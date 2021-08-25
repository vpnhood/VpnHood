using VpnHood.Server;
using System.Threading.Tasks;
using System;
using VpnHood.Server.AccessServers;
using VpnHood.Common.Messaging;
using System.Net;

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
        public Task Server_SetStatus(ServerStatus serverStatus) => _restAccessServer.Server_SetStatus(serverStatus);
        public Task Server_Subscribe(ServerInfo serverInfo) => _restAccessServer.Server_Subscribe(serverInfo);
        public Task<SessionResponseEx> Session_Get(uint sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp) => _restAccessServer.Session_Get(sessionId, hostEndPoint, clientIp);

        public Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx) => _restAccessServer.Session_Create(sessionRequestEx);

        public Task<ResponseBase> Session_AddUsage(uint sessionId, bool closeSession, UsageInfo usageInfo) => _restAccessServer.Session_AddUsage(sessionId, closeSession, usageInfo);

        public Task<byte[]> GetSslCertificateData(IPEndPoint hostEndPoint) => _restAccessServer.GetSslCertificateData(hostEndPoint);

        public void Dispose()
        {
            _restAccessServer.Dispose();
            EmbedIoAccessServer.Dispose();
            BaseAccessServer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
