using System.Net;

namespace VpnHood.Server
{
    public class TrackingOptions
    {
        public bool LocalPort { get; set; }
        public bool ClientIp { get; set; }

        public bool IsEnabled() => ClientIp || LocalPort;
    }
}