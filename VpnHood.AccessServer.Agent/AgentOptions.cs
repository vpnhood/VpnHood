using VpnHood.Server.Configurations;
using SessionOptions = VpnHood.Server.Configurations;

namespace VpnHood.AccessServer.Agent;

public class AgentOptions
{
    public TimeSpan ServerUpdateStatusInterval { get; set; } = new ServerConfig().UpdateStatusIntervalValue;
    public TimeSpan LostServerThreshold => ServerUpdateStatusInterval * 3;
    public TimeSpan SessionSyncInterval { get; set; } = new SessionOptions.SessionOptions().SyncIntervalValue;
    public TimeSpan SessionTemporaryTimeout { get; set; } = new SessionOptions.SessionOptions().TimeoutValue;
    public TimeSpan SessionPermanentlyTimeout { get; set; } = TimeSpan.FromDays(2);
    public TimeSpan SaveCacheInterval { get; set; } = TimeSpan.FromMinutes(5);
    public string SystemAuthorizationCode { get; set; } = "";
    public bool AllowRedirect { get; set; } = true;
}