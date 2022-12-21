using System.Net;
using VpnHood.Server;
using SessionOptions = VpnHood.Server.SessionOptions;

namespace VpnHood.AccessServer.Agent;

public class AgentOptions
{
    public TimeSpan ServerUpdateStatusInterval { get; set; } = new ServerConfig(Array.Empty<IPEndPoint>(), "" ).UpdateStatusInterval;
    public TimeSpan LostServerThreshold => ServerUpdateStatusInterval * 3;
    public TimeSpan SessionSyncInterval { get; set; } = new SessionOptions().SyncInterval;
    public TimeSpan SessionTimeout { get; set; } = new SessionOptions().Timeout;
    public TimeSpan SaveCacheInterval { get; set; } = TimeSpan.FromMinutes(5);
    public string SystemAuthorizationCode { get; set; } = "";
    public bool AllowRedirect { get; set; } = true;
}