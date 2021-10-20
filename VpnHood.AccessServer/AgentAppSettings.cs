using System;
using VpnHood.Server.AccessServers;

namespace VpnHood.AccessServer
{
    public class AgentAppSettings
    {
        public RestAccessServerOptions RestAccessServer { get; set; }
        public byte[] Secret { get; set; }

        public AgentAppSettings(RestAccessServerOptions restAccessServer, byte[] secret)
        {
            RestAccessServer = restAccessServer;
            Secret = secret;
        }
    }
}