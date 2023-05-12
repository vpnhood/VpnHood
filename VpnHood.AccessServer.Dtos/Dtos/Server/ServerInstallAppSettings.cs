using System.Text.Json.Serialization;
using VpnHood.Server.Providers.HttpAccessServerProvider;

namespace VpnHood.AccessServer.Dtos;

public class ServerInstallAppSettings
{
    public HttpAccessServerOptions HttpAccessServer { get; set; }

    [JsonPropertyName("Secret")]
    public byte[] ManagementSecret { get; set; }

    public ServerInstallAppSettings(HttpAccessServerOptions httpAccessServer, byte[] managementSecret)
    {
        HttpAccessServer = httpAccessServer;
        ManagementSecret = managementSecret;
    }
}