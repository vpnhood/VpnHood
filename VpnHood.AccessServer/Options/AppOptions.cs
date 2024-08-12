namespace VpnHood.AccessServer.Options;

public class AppOptions
{
    public const string AgentHttpClientName = "AgentHttpClient";
    public TimeSpan AutoMaintenanceInterval { get; set; } = TimeSpan.FromMinutes(5);
    public Uri AgentUrl { get; set; } = default!;
    public Uri? AgentUrlPrivate { get; set; }
    public string AgentSystemAuthorization { get; set; } = default!;
    public TimeSpan ServerUpdateStatusInterval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan LostServerThreshold => ServerUpdateStatusInterval * 3;
    public TimeSpan ServiceHttpTimeout => TimeSpan.FromSeconds(30);
    public int HostOrderMonitorCount { get; set; } = 2 * 10;
    public TimeSpan HostOrderMonitorInterval { get; set; } = TimeSpan.FromSeconds(30);
}