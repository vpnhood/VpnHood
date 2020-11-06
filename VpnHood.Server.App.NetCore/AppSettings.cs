using System;

namespace VpnHood.Server.App
{
    class AppSettings
    {
        public Uri RestBaseUrl { get; set; }
        public string RestAuthHeader { get; set; }
        public ushort Port { get; set; } = 443;
    }
}
