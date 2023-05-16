using VpnHood.Server.Providers.HttpAccessServerProvider;

namespace VpnHood.AccessServer.Dtos;

public class ServerInstallAppSettings
{
    public required HttpAccessServerOptions HttpAccessServer { get; init; }

    public required byte[] ManagementSecret { get; init; }
}