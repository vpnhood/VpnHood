using System;

namespace VpnHood.AccessServer;

public class AppOptions
{
    public const string Name = "Authorization Server";
    public const string AuthIssuer = "auth.vpnhood.com";
    public const string AuthAudience = "access.vpnhood.com";
    public const string AuthRobotScheme = "Robot";

    public TimeSpan? AutoMaintenanceInterval { get; set; }
    public byte[] AuthenticationKey { get; set; } = Array.Empty<byte>();
    public TimeSpan ServerUpdateStatusInterval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan LostServerThreshold => ServerUpdateStatusInterval * 3;
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan SessionCacheTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public Uri? AgentUri { get; set; }

}