using System.Net;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.VpnAdapters.Abstractions;

/// <summary>
/// Provides the options of one run of a VPN adapter, from its start to its stop; each restart uses them again.
/// </summary>
public class VpnAdapterOptions
{
    /// <summary>
    /// Gets the two IPv4 halves, 0.0.0.0/1 and 128.0.0.0/1, which route every IPv4 address through the interface
    /// while staying more specific than the host's default route.
    /// </summary>
    public static IReadOnlyList<IpNetwork> AllVRoutesIpV4 => field ??= [IpNetwork.Parse("0.0.0.0/1"), IpNetwork.Parse("128.0.0.0/1")];

    /// <summary>
    /// Gets the two IPv6 halves, ::/1 and 8000::/1, which route every IPv6 address through the interface while
    /// staying more specific than the host's default route.
    /// </summary>
    public static IReadOnlyList<IpNetwork> AllVRoutesIpV6 => field ??= [IpNetwork.Parse("::/1"), IpNetwork.Parse("8000::/1")];

    /// <summary>
    /// Gets <see cref="AllVRoutesIpV4"/> and <see cref="AllVRoutesIpV6"/> together.
    /// </summary>
    public static IReadOnlyList<IpNetwork> AllVRoutes => field ??= AllVRoutesIpV4.Concat(AllVRoutesIpV6).ToArray();

    /// <summary>
    /// Gets the VPN server's address, for an adapter whose OS asks for it, or <see langword="null"/>.
    /// </summary>
    public IPAddress? ServerIp { get; init; }

    /// <summary>
    /// Gets the session's name, which the OS shows where it can, or <see langword="null"/> or empty for none.
    /// </summary>
    public required string? SessionName { get; init; }

    /// <summary>
    /// Gets the interface's IPv4 address with its network in the tunnel, or <see langword="null"/> to carry no
    /// IPv4.
    /// </summary>
    public IpNetwork? VirtualIpNetworkV4 { get; init; }

    /// <summary>
    /// Gets the interface's IPv6 address with its network in the tunnel, or <see langword="null"/> to carry no
    /// IPv6. When the address cannot be added, the run goes on without IPv6.
    /// </summary>
    public IpNetwork? VirtualIpNetworkV6 { get; init; }

    /// <summary>
    /// Gets the DNS servers the interface gives the host. Those of an IP version the interface does not carry are
    /// left out. The default is none.
    /// </summary>
    public IReadOnlyList<IPAddress> DnsServers { get; init; } = [];

    /// <summary>
    /// Gets the networks routed through the tunnel. The default is none.
    /// </summary>
    /// <remarks>
    /// With <see cref="VpnAdapterSettings.AutoMetric"/>, networks that cover every address of an IP version are
    /// routed as its two halves (<see cref="AllVRoutesIpV4"/>, <see cref="AllVRoutesIpV6"/>). Where the adapter leaves
    /// the probe networks out of its routes, that comes first, and what remains is routed as it is.
    /// </remarks>
    public IEnumerable<IpNetwork> IncludeNetworks { get; init; } = [];

    /// <summary>
    /// Gets the apps whose traffic stays out of the tunnel, by package ID, or <see langword="null"/> for none.
    /// This app itself is never left out.
    /// </summary>
    /// <remarks>
    /// Only an adapter whose <see cref="TunVpnAdapter.IsAppFilterSupported"/> is <see langword="true"/> applies
    /// it. It cannot be non-empty together with <see cref="IncludeApps"/>.
    /// </remarks>
    public IEnumerable<string>? ExcludeApps { get; init; }

    /// <summary>
    /// Gets the only apps whose traffic goes into the tunnel, by package ID, or <see langword="null"/> for every
    /// app. This app itself is always added.
    /// </summary>
    /// <remarks>
    /// Only an adapter whose <see cref="TunVpnAdapter.IsAppFilterSupported"/> is <see langword="true"/> applies
    /// it. It cannot be non-empty together with <see cref="ExcludeApps"/>.
    /// </remarks>
    public IEnumerable<string>? IncludeApps { get; init; }

    /// <summary>
    /// Gets a value indicating whether the adapter translates the virtual networks to the host's primary
    /// addresses (NAT), as a server does. The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// A start with it fails when the adapter's NAT does not translate IPv4; IPv6 is translated only where the
    /// adapter supports it.
    /// </remarks>
    public bool UseNat { get; init; }

    /// <summary>
    /// Gets the interface's MTU in bytes, or <see langword="null"/> to leave the system's. It also sizes the
    /// buffer each packet is read into, which holds 65,535 bytes in a run without it.
    /// </summary>
    public int? Mtu { get; init; }

    /// <summary>
    /// Gets the interface's route metric, or <see langword="null"/> to leave the system's.
    /// </summary>
    public int? Metric { get; init; }
}
