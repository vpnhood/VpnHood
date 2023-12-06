using Ga4.Ga4Tracking;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.SystemInformation;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Server;

public class ServerOptions
{
    public SocketFactory SocketFactory { get; set; } = new();
    public Ga4Tracker? GaTracker { get; set; }
    public ISystemInfoProvider? SystemInfoProvider { get; set; }
    public INetFilter NetFilter { get; set; } = new NetFilter();
    public bool AutoDisposeAccessManager { get; set; } = true;
    public TimeSpan ConfigureInterval { get; set; } = TimeSpan.FromSeconds(60);
    public string StoragePath { get; set; } = Directory.GetCurrentDirectory();
    public bool PublicIpDiscovery { get; set; } = true;
    public ServerConfig? Config { get; set; }
}