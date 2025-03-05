using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public class TunVpnAdapterOptions
{
    public static readonly IpNetwork[] AllVRoutesIpV4 = [IpNetwork.Parse("0.0.0.0/1"), IpNetwork.Parse("128.0.0.0/1")];
    public static readonly IpNetwork[] AllVRoutesIpV6 = [IpNetwork.Parse("::/1"), IpNetwork.Parse("8000::/1")];
    public static readonly IpNetwork[] AllVRoutes = AllVRoutesIpV4.Concat(AllVRoutesIpV6).ToArray();
    public required string AdapterName { get; init; }
    public ILogger Logger { get; init; } = VhLogger.Instance;
    public int MaxPacketCount { get; init; } = 255;
    public TimeSpan MaxPacketSendDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public int MaxAutoRestartCount { get; init; } = 0;
}