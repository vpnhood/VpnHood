using VpnHood.Server.Providers.HttpAccessServerProvider;

namespace VpnHood.AccessServer.Dtos.ServerDtos;

public class ServerInstallAppSettings
{
    public HttpAccessServerOptions HttpAccessServer { get; set; }

    public byte[] Secret { get; set; }

    public ServerInstallAppSettings(HttpAccessServerOptions httpAccessServer, byte[] secret)
    {
        HttpAccessServer = httpAccessServer;
        Secret = secret;
    }
}