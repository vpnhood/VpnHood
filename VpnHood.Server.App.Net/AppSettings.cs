using System;

namespace VpnHood.Server.App
{
    class AppSettings
    {
        public Guid? ServerId { get; set; }
        public Uri RestBaseUrl { get; set; }
        public string RestAuthorization { get; set; }
        public string RestCertificateThumbprint { get; set; }
        public ushort Port { get; set; } = 443;
        public bool IsAnonymousTrackerEnabled { get; set; } = true;
        public string SslCertificatesPassword { get; set; }
        public bool IsDiagnoseMode { get; set; }
        public int OrgStreamReadBufferSize { get; set; } = new ServerOptions().OrgStreamReadBufferSize;
        public int TunnelStreamReadBufferSize { get; set; } = new ServerOptions().TunnelStreamReadBufferSize;
        public int MaxDatagramChannelCount { get; set; } = new ServerOptions().MaxDatagramChannelCount;
    }
}
