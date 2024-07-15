using Ga4.Trackers;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.SystemInformation;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Server;

public class ServerOptions
{
    public SocketFactory SocketFactory { get; init; } = new();
    public ITracker? Tracker { get; init; }
    public ISystemInfoProvider? SystemInfoProvider { get; init; }
    public INetFilter NetFilter { get; init; } = new NetFilter();
    public bool AutoDisposeAccessManager { get; init; } = true;
    public TimeSpan ConfigureInterval { get; init; } = TimeSpan.FromSeconds(60);
    public string StoragePath { get; init; } = Directory.GetCurrentDirectory();
    public bool PublicIpDiscovery { get; init; } = true;
    public ServerConfig? Config { get; init; }
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(1);
}