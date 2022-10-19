using System;

namespace VpnHood.AccessServer;

public class AppOptions
{
    public const string AgentHttpClientName = "AgentHttpClient";
    public TimeSpan? AutoMaintenanceInterval { get; set; }
    public Uri AgentUri { get; set; } = default!;
    public string AgentAuthorization { get; set; } = default!;
    public TimeSpan ServerUpdateStatusInterval { get; set; } = TimeSpan.FromMinutes(10); //todo rename to stat delay
    public TimeSpan LostServerThreshold { get; set; } = TimeSpan.FromMinutes(2); //todo rename to stat delay
}