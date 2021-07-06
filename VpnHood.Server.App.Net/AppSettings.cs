using System;

namespace VpnHood.Server.App
{
    class AppSettings
    {
        public Uri RestBaseUrl { get; set; }
        public string RestAuthHeader { get; set; }
        public string RestCertificateThumbprint { get; set; }
        public ushort Port { get; set; } = 443;
        public bool IsAnonymousTrackerEnabled { get; set; } = true;
        public string SslCertificatesPassword { get; set; }
        public bool IsDiagnoseMode { get; set; }
        public int OrgStreamReadBufferSize { get; set; } = 0x14000 / 2;
        public int TunnelStreamReadBufferSize { get; set; } = 0x14000 / 4;
    }
}
