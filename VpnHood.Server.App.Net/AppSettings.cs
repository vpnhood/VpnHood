using VpnHood.Server.AccessServers;

namespace VpnHood.Server.App
{
    public class AppSettings
    {
        public RestAccessServerOptions? RestAccessServer { get; set; }
        public FileAccessServerOptions? FileAccessServer { get; set; } = new();
        public bool IsAnonymousTrackerEnabled { get; set; } = true;
        public bool IsDiagnoseMode { get; set; }
    }
}