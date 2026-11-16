using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.Access.Configurations;

/// <summary>
/// Represents which destinations the server's clients may reach, and what their VPN adapters capture.
/// </summary>
/// <remarks>
/// A setting left <see langword="null"/> takes its default, which the property of its name ending in <c>Value</c>
/// gives. The DNS servers the server gives its clients are let through these ranges, though the isolation, local-network
/// and port blocks still apply to them: a DNS server at an address that is not public works only with
/// <see cref="IncludeLocalNetwork"/>.
/// </remarks>
public class NetFilterOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the server keeps its clients from reaching each other's virtual
    /// addresses, or <see langword="null"/> for <see langword="true"/>.
    /// </summary>
    public bool? NetworkIsolation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the clients may reach the server's local networks and its own addresses,
    /// and their VPN adapters capture local networks, or <see langword="null"/> for <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Without it the server blocks its own addresses and every network that is not public
    /// (<see cref="IpNetwork.NonPublicNetworks"/>): its loopback, its local networks, and its provider's network,
    /// such as the carriers' shared space, where a cloud's instance metadata may be. With it all of them are open,
    /// the loopback too, so a service that listens there for the machine alone is the clients' to reach.
    /// </remarks>
    public bool? IncludeLocalNetwork { get; set; }

    /// <summary>
    /// Gets or sets the only destinations the clients' VPN adapters capture, or <see langword="null"/> or empty for all
    /// of them.
    /// </summary>
    public IpRange[]? VpnAdapterIncludeIpRanges { get; set; }

    /// <summary>
    /// Gets or sets the destinations the clients' VPN adapters leave out, or <see langword="null"/> for none.
    /// </summary>
    public IpRange[]? VpnAdapterExcludeIpRanges { get; set; }

    /// <summary>
    /// Gets or sets the only destinations the server routes, which it tells its clients, or <see langword="null"/> or
    /// empty for all of them.
    /// </summary>
    public IpRange[]? IncludeIpRanges { get; set; }

    /// <summary>
    /// Gets or sets the destinations the server does not route, or <see langword="null"/> for none.
    /// </summary>
    public IpRange[]? ExcludeIpRanges { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server blocks IPv6 and tells its clients it carries none, or
    /// <see langword="null"/> for <see langword="false"/>.
    /// </summary>
    public bool? BlockIpV6 { get; set; }

    /// <summary>
    /// Gets or sets the TCP destination ports the server blocks, or <see langword="null"/> for port 25 alone.
    /// </summary>
    public IReadOnlyList<PortRange>? BlockTcpPorts { get; set; }

    /// <summary>
    /// Gets or sets the UDP destination ports the server blocks, or <see langword="null"/> for none.
    /// </summary>
    public IReadOnlyList<PortRange>? BlockUdpPorts { get; set; }

    /// <summary>Gets <see cref="NetworkIsolation"/>, or its default, <see langword="true"/>.</summary>
    [JsonIgnore]
    public bool NetworkIsolationValue => NetworkIsolation ?? true;

    /// <summary>Gets <see cref="IncludeLocalNetwork"/>, or its default, <see langword="false"/>.</summary>
    [JsonIgnore]
    public bool IncludeLocalNetworkValue => IncludeLocalNetwork ?? false;

    /// <summary>Gets <see cref="BlockIpV6"/>, or its default, <see langword="false"/>.</summary>
    [JsonIgnore]
    public bool BlockIpV6Value => BlockIpV6 ?? false;

    /// <summary>
    /// Gets <see cref="BlockTcpPorts"/>, or its default, port 25 alone, as a user's mail to port 25 can get the server's
    /// IP blocked as a spam source. A list that is set replaces it.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<PortRange> BlockTcpPortsValue => BlockTcpPorts ?? [new PortRange(25)];

    /// <summary>Gets <see cref="BlockUdpPorts"/>, or its default, none.</summary>
    [JsonIgnore]
    public IReadOnlyList<PortRange> BlockUdpPortsValue => BlockUdpPorts ?? [];

    /// <summary>Copies over these options each one another set sets.</summary>
    /// <param name="obj">The options that win where they are set.</param>
    public void Merge(NetFilterOptions obj)
    {
        if (obj.NetworkIsolation != null) NetworkIsolation = obj.NetworkIsolation;
        if (obj.IncludeLocalNetwork != null) IncludeLocalNetwork = obj.IncludeLocalNetwork;
        if (obj.VpnAdapterIncludeIpRanges != null) VpnAdapterIncludeIpRanges = obj.VpnAdapterIncludeIpRanges;
        if (obj.VpnAdapterExcludeIpRanges != null) VpnAdapterExcludeIpRanges = obj.VpnAdapterExcludeIpRanges;
        if (obj.IncludeIpRanges != null) IncludeIpRanges = obj.IncludeIpRanges;
        if (obj.ExcludeIpRanges != null) ExcludeIpRanges = obj.ExcludeIpRanges;
        if (obj.BlockIpV6 != null) BlockIpV6 = obj.BlockIpV6;
        if (obj.BlockTcpPorts != null) BlockTcpPorts = obj.BlockTcpPorts;
        if (obj.BlockUdpPorts != null) BlockUdpPorts = obj.BlockUdpPorts;
    }

    /// <summary>
    /// Sets each option left <see langword="null"/> to its default; the ranges, which have none, stay as they are.
    /// </summary>
    public void ApplyDefaults()
    {
        NetworkIsolation = NetworkIsolationValue;
        IncludeLocalNetwork = IncludeLocalNetworkValue;
        BlockIpV6 = BlockIpV6Value;
        BlockTcpPorts = BlockTcpPortsValue;
        BlockUdpPorts = BlockUdpPortsValue;
    }

    /// <summary>
    /// Gets the destinations the server routes: every address, narrowed to <see cref="IncludeIpRanges"/> where it is
    /// set and not empty, less <see cref="ExcludeIpRanges"/>.
    /// </summary>
    /// <returns>The ranges.</returns>
    public IpRangeOrderedList GetFinalIncludeIpRanges()
    {
        var includeIpRanges = IpNetwork.All.ToIpRanges();
        if (!VhUtils.IsNullOrEmpty(IncludeIpRanges))
            includeIpRanges = includeIpRanges.Intersect(IncludeIpRanges);

        if (!VhUtils.IsNullOrEmpty(ExcludeIpRanges))
            includeIpRanges = includeIpRanges.Exclude(ExcludeIpRanges);

        return includeIpRanges;
    }

    /// <summary>
    /// Gets the destinations the clients' VPN adapters capture: every address but the local networks, unless
    /// <see cref="IncludeLocalNetwork"/>, narrowed to <see cref="VpnAdapterIncludeIpRanges"/> where it is set and not
    /// empty, less <see cref="VpnAdapterExcludeIpRanges"/>.
    /// </summary>
    /// <returns>The ranges.</returns>
    public IEnumerable<IpRange> GetFinalVpnAdapterIncludeIpRanges()
    {
        var vpnAdapterIncludeIpRanges = IpNetwork.All.ToIpRanges();
        if (!IncludeLocalNetworkValue)
            vpnAdapterIncludeIpRanges = vpnAdapterIncludeIpRanges.Exclude(IpNetwork.LocalNetworks.ToIpRanges());

        if (!VhUtils.IsNullOrEmpty(VpnAdapterIncludeIpRanges))
            vpnAdapterIncludeIpRanges = vpnAdapterIncludeIpRanges.Intersect(VpnAdapterIncludeIpRanges);

        if (!VhUtils.IsNullOrEmpty(VpnAdapterExcludeIpRanges))
            vpnAdapterIncludeIpRanges = vpnAdapterIncludeIpRanges.Exclude(VpnAdapterExcludeIpRanges);

        return vpnAdapterIncludeIpRanges;
    }

    /// <summary>
    /// Gets the destinations these ranges block: all that the server does not route or the adapters do not capture, and
    /// IPv6 under <see cref="BlockIpV6"/>.
    /// </summary>
    /// <returns>The ranges.</returns>
    /// <remarks>
    /// The server lets its DNS servers out of these ranges, then blocks more besides, DNS servers in them included: its
    /// clients' virtual addresses under <see cref="NetworkIsolation"/>, and its own addresses and every network that
    /// is not public (<see cref="IpNetwork.NonPublicNetworks"/>) unless <see cref="IncludeLocalNetwork"/>; the blocked
    /// ports apply too.
    /// </remarks>
    public IpRangeOrderedList GetBlockedIpRanges()
    {
        var includeIpRanges = GetFinalIncludeIpRanges().Intersect(GetFinalVpnAdapterIncludeIpRanges());
        if (BlockIpV6Value)
            includeIpRanges = includeIpRanges.Exclude(IpNetwork.AllV6.ToIpRange());

        return includeIpRanges.Invert();
    }
}