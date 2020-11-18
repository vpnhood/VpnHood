using System;

namespace VpnHood.Server.App
{
    class AppSettings
    {
        public Uri RestBaseUrl { get; set; }
        public string RestAuthHeader { get; set; }
        public ushort Port { get; set; } = 443;
        public bool IsAnonymousTrackerEnabled { get; set; } = true;
        public string SslCertificateFile { get; set; }
        public string SslCertificatesPassword { get; set; }
    }
}
