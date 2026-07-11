using System.Net;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Messaging;

namespace VpnHood.Core.Server.Access.Managers;

public interface IAccessManager : IDisposable
{
    bool IsMaintenanceMode { get; }
    Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx, CancellationToken cancellationToken);
    Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp, CancellationToken cancellationToken);
    Task<SessionResponseEx[]> Session_GetAll(CancellationToken cancellationToken);
    Task<SessionResponse> Session_AddUsage(ulong sessionId, Traffic traffic, string? adData, CancellationToken cancellationToken);
    Task<Dictionary<ulong, SessionResponse>> Session_AddUsages(SessionUsage[] sessionUsages, CancellationToken cancellationToken);
    Task<SessionResponse> Session_Close(ulong sessionId, Traffic traffic, CancellationToken cancellationToken);
    Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus, CancellationToken cancellationToken);
    Task<ServerConfig> Server_Configure(ServerInfo serverInfo, CancellationToken cancellationToken);
    Task<string> Acme_GetHttp01KeyAuthorization(string token, CancellationToken cancellationToken);
}