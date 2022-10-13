namespace VpnHood.AccessServer.Agent;

public class AgentOptions
{
    public const string Name = "Authorization Server";
    public const string AuthIssuer = "auth.vpnhood.com";
    public const string AuthAudience = "access.vpnhood.com";
    public const string AuthRobotScheme = "Robot";

    public byte[] AuthenticationKey { get; set; } = Array.Empty<byte>();
    public TimeSpan ServerUpdateStatusInterval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan LostServerThreshold => ServerUpdateStatusInterval * 3;
    public TimeSpan SessionSyncInterval { get; set; }
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan SessionCacheTimeout { get; set; } = TimeSpan.FromMinutes(10);
}