using System;
using VpnHood.Server.Configurations;
using VpnHood.Server.Providers.FileAccessServerProvider;
using VpnHood.Server.Providers.HttpAccessServerProvider;

namespace VpnHood.Server.App;


public class AppSettings
{
    [Obsolete ("Use HttpAccessServer")]
    public HttpAccessServerOptions? RestAccessServer { get=> HttpAccessServer; set => HttpAccessServer = value;}
    public HttpAccessServerOptions? HttpAccessServer { get; set; }
    public FileAccessServerOptions? FileAccessServer { get; set; } = new();
    public ServerConfig? ServerConfig { get; set; }
    public bool IsAnonymousTrackerEnabled { get; set; } = true;
    public bool IsDiagnoseMode { get; set; }
}
