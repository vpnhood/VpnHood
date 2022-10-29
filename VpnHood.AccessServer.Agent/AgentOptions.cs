namespace VpnHood.AccessServer.Agent;

public class AgentOptions
{
    public TimeSpan ServerUpdateStatusInterval { get; set; } = new Server.ServerConfig(Array.Empty<System.Net.IPEndPoint>(), "" ).UpdateStatusInterval;
    public TimeSpan LostServerThreshold => ServerUpdateStatusInterval * 3;
    public TimeSpan SessionSyncInterval { get; set; } = new Server.SessionOptions().SyncInterval;
    public TimeSpan SessionTimeout { get; set; } = new Server.SessionOptions().Timeout;
    public TimeSpan SessionCacheTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan SaveCacheInterval { get; set; } = TimeSpan.FromMinutes(5);
    public string SystemAuthorizationCode { get; set; } = "";
    public bool AllowRedirect { get; set; } = true;
}