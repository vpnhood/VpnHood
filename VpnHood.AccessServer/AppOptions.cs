using System;

namespace VpnHood.AccessServer;

public class AppOptions
{
    public const string AgentHttpClientName = "AgentHttpClient";
    public TimeSpan? AutoMaintenanceInterval { get; set; }
    public Uri AgentUri { get; set; } = default!;
    public string AgentSystemAuthorization { get; set; } = default!;
    public TimeSpan ServerUpdateStatusInterval { get; set; } = TimeSpan.FromSeconds(60); //todo rename to stat delay
    public TimeSpan LostServerThreshold => ServerUpdateStatusInterval * 3; //todo rename to stat delay
}