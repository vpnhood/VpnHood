using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers.File;
using VpnHood.Server.Access.Managers.Http;

namespace VpnHood.Server.App;


public class AppSettings
{
    [Obsolete ("Use HttpAccessManager")]
    public HttpAccessManagerOptions? RestAccessServer { get=> HttpAccessManager; set => HttpAccessManager = value;}
    [Obsolete ("Use HttpAccessManager")]
    public HttpAccessManagerOptions? HttpAccessServer { get=> HttpAccessManager; set => HttpAccessManager = value;}
    [Obsolete("Use FileAccessManager")]
    public FileAccessManagerOptions? FileAccessServer { get => FileAccessManager; set => FileAccessManager = value; }

    public HttpAccessManagerOptions? HttpAccessManager { get; set; }
    public FileAccessManagerOptions? FileAccessManager { get; set; } = new();
    public ServerConfig? ServerConfig { get; set; }
    public bool AllowAnonymousTracker { get; set; } = true;
    public bool IsDiagnoseMode { get; set; }
}
