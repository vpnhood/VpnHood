namespace VpnHood.AccessServer.Agent;

public class AgentOptions
{
    public TimeSpan ServerUpdateStatusInterval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan LostServerThreshold => ServerUpdateStatusInterval * 3;
    public TimeSpan SessionSyncInterval { get; set; }
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan SessionCacheTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan SaveCacheInterval { get; set; } = TimeSpan.FromMinutes(5);
    public string SystemAuthorizationCode { get; set; } = "";
}