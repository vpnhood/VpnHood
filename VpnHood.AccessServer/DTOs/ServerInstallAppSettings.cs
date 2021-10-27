using System;
using VpnHood.Server.AccessServers;

namespace VpnHood.AccessServer.DTOs
{
    public class ServerInstallAppSettings
    {
        public RestAccessServerOptions RestAccessServer { get; set; }
        public byte[] Secret { get; set; }

        public ServerInstallAppSettings(RestAccessServerOptions restAccessServer, byte[] secret)
        {
            RestAccessServer = restAccessServer;
            Secret = secret;
        }
    }
}