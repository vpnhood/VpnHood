using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;
using VpnHood.Core.Server.Access.Managers.HttpAccessManagers;

namespace VpnHood.App.Server;

public class AppSettings
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Obsolete("Use HttpAccessManager")]
    public HttpAccessManagerOptions? RestAccessServer {
        set => HttpAccessManager = value;
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Obsolete("Use HttpAccessManager")]
    public HttpAccessManagerOptions? HttpAccessServer {
        set => HttpAccessManager = value;
    }

    [Obsolete("Use FileAccessManager")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public FileAccessManagerOptions? FileAccessServer {
        set => FileAccessManager = value;
    }

    [Obsolete("Use ManagementSecret")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public string? Secret {
        set => ManagementSecret = value;
    }

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