using System;
using Microsoft.Extensions.Configuration;
using VpnHood.Common;

namespace VpnHood.AccessServer;

public class AccessServerApp : AppBaseNet<AccessServerApp>
{
    public Uri AgentUrl { get; set; } = null!;
    public TimeSpan ServerUpdateStatusInterval { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan LostServerThreshold => ServerUpdateStatusInterval * 3;
    public bool AutoMaintenance { get; set; }

    public AccessServerApp() : base("VpnHoodAccessServer")
    {
        // create logger
    }

    public void Configure(IConfiguration configuration)
    {
        //load settings
        ServerUpdateStatusInterval = TimeSpan.FromSeconds(configuration.GetValue(nameof(ServerUpdateStatusInterval), ServerUpdateStatusInterval.TotalSeconds));
        AutoMaintenance = configuration.GetValue<bool>(nameof(AutoMaintenance));
        var agentUrl = configuration.GetValue(nameof(AgentUrl), "");
        AgentUrl = !string.IsNullOrEmpty(agentUrl) ? new Uri(agentUrl) : throw new InvalidOperationException($"Could not read {nameof(AgentUrl)} from settings.");
    }

    protected override void OnStart(string[] args)
    {
    }
}