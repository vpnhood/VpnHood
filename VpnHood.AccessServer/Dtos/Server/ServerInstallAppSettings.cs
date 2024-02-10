
using VpnHood.Server.Access.Managers.Http;

namespace VpnHood.AccessServer.Dtos.Server;

public class ServerInstallAppSettings
{
    public required HttpAccessManagerOptions HttpAccessManager { get; init; }

    public required byte[] ManagementSecret { get; init; }
}