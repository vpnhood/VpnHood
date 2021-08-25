using System;
using System.Net;
using System.Threading.Tasks;
using VpnHood.Common.Messaging;

namespace VpnHood.Server
{
    public interface IAccessServer : IDisposable
    {
        bool IsMaintenanceMode { get; }
        Task<SessionResponse> Session_Create(SessionRequestEx sessionRequestEx);
        Task<SessionResponse> Session_Get(uint sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp);
        Task<ResponseBase> Session_AddUsage(uint sessionId, bool closeSession, UsageInfo usageInfo);
        Task Server_SetStatus(ServerStatus serverStatus);
        Task Server_Subscribe(ServerInfo serverInfo);
        Task<byte[]> GetSslCertificateData(IPEndPoint hostEndPoint);
    }
}
