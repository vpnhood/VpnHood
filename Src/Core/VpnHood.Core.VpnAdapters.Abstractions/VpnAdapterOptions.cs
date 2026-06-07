using System.Net;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public class VpnAdapterOptions
{
    public static IReadOnlyList<IpNetwork> AllVRoutesIpV4 => field ??= [IpNetwork.Parse("0.0.0.0/1"), IpNetwork.Parse("128.0.0.0/1")];
    public static IReadOnlyList<IpNetwork> AllVRoutesIpV6 => field ??= [IpNetwork.Parse("::/1"), IpNetwork.Parse("8000::/1")];
    public static IReadOnlyList<IpNetwork> AllVRoutes => field ??= AllVRoutesIpV4.Concat(AllVRoutesIpV6).ToArray();

    public IPAddress? ServerIp { get; init; }
    public required string? SessionName { get; init; }
    public IpNetwork? VirtualIpNetworkV4 { get; init; }
    public IpNetwork? VirtualIpNetworkV6 { get; init; }
    public IReadOnlyList<IPAddress> DnsServers { get; init; } = [];
    public IEnumerable<IpNetwork> IncludeNetworks { get; init; } = [];
    public IEnumerable<string>? ExcludeApps { get; init; }
    public IEnumerable<string>? IncludeApps { get; init; }
    public bool UseNat { get; init; }
    public int? Mtu { get; init; }
    public int? Metric { get; init; }
}