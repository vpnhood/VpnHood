using System.Text.Json.Serialization;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers.File;
using VpnHood.Server.Access.Managers.Http;

namespace VpnHood.Server.App;


public class AppSettings
{
    [Obsolete("Use HttpAccessManager")]
    public HttpAccessManagerOptions? RestAccessServer { set => HttpAccessManager = value; }
    [Obsolete("Use HttpAccessManager")]
    public HttpAccessManagerOptions? HttpAccessServer { set => HttpAccessManager = value; }
    [Obsolete("Use FileAccessManager")]
    public FileAccessManagerOptions? FileAccessServer { set => FileAccessManager = value; }
    [Obsolete("Use ManagementSecret")]
    public string? Secret { set => ManagementSecret = value; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public HttpAccessManagerOptions? HttpAccessManager { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public FileAccessManagerOptions? FileAccessManager { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ServerConfig? ServerConfig { get; set; }

    public bool AllowAnonymousTracker { get; set; } = true;
    public bool IsDiagnoseMode { get; set; }

    public string? ManagementSecret { get; set; }
}
