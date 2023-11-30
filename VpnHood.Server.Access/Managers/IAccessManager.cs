using System.Net;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.Server.Access.Managers;

public interface IAccessManager : IDisposable
{
    bool IsMaintenanceMode { get; }
    Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx);
    Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp);
    Task<SessionResponseBase> Session_AddUsage(ulong sessionId, Traffic traffic);
    Task<SessionResponseBase> Session_Close(ulong sessionId, Traffic traffic);
    Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus);
    Task<ServerConfig> Server_Configure(ServerInfo serverInfo);
    Task<byte[]> GetSslCertificateData(IPEndPoint hostEndPoint);
}