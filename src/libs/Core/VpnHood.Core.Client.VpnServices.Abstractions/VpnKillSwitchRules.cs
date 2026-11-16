using System.Net;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Represents what a kill switch's block lets out besides the tunnel and the VPN service: the device-level split only,
/// what the adapter's routes leave out.
/// </summary>
/// <remarks>
/// The app-level splits (country, IP via app, domain) are the service's: it sends their traffic out itself while
/// connected, which the block lets through as the service's own, so none of their addresses goes into the block, and
/// while blocked with no tunnel they wait for it. A country's thousands of ranges would be too many for the OS's
/// firewall anyway.
/// </remarks>
public class VpnKillSwitchRules
{
    /// <summary>
    /// Gets the tunnel's adapter, by the name its settings give it
    /// (<see cref="VpnHood.Core.Client.Abstractions.ClientOptions.AdapterName"/>): its traffic passes once it is up.
    /// </summary>
    public required string AdapterName { get; init; }

    /// <summary>
    /// Gets what the adapter's routes leave out, which passes: the local networks unless they are included, multicast,
    /// broadcast, and the person's IP-via-device list; not the server's address, which only the service reaches.
    /// </summary>
    public required IpRangeOrderedList PassIpRanges { get; init; }

    /// <summary>
    /// Gets the resolvers DNS may reach outside the tunnel: an out-of-tunnel plan's. Every other plain DNS query, to
    /// port 53, outside it is blocked, a LAN router's too, which would otherwise learn every name; where a platform's
    /// block cannot see the port, as for traffic Windows routes itself, one to an address that passes passes.
    /// </summary>
    public required IReadOnlyList<IPAddress> DnsServers { get; init; }

    /// <summary>
    /// Creates the rules by the person's settings, with no session: a connect's block until its adapter starts, and the
    /// app's own while disconnected with the block kept (the app's <c>KillSwitchMode.Lockdown</c>).
    /// </summary>
    /// <param name="adapterName">The name of the tunnel's adapter.</param>
    /// <param name="includeIpRangesByDevice">
    /// The addresses the device's tunnel captures
    /// (<see cref="VpnHood.Core.Client.Abstractions.ClientOptions.IncludeIpRangesByDevice"/>); the rest passes.
    /// </param>
    /// <param name="splitLocalNetwork">
    /// <see langword="true"/> if the local networks, multicast and broadcast stay out of the tunnel, and so pass.
    /// </param>
    /// <returns>The rules, with no resolver passing.</returns>
    /// <remarks>
    /// The server's own exclusions come only with a session, and so does its DNS plan, so no resolver passes, nor the
    /// local networks unless the person's split leaves them out — the replies to an inbound connection too, SSH's into a
    /// locked-down box.
    /// </remarks>
    public static VpnKillSwitchRules CreateBySettings(string adapterName,
        IReadOnlyList<IpRange> includeIpRangesByDevice, bool splitLocalNetwork)
    {
        var includeIpRanges = includeIpRangesByDevice.ToOrderedList();
        if (splitLocalNetwork) {
            includeIpRanges = includeIpRanges
                .Exclude(IpNetwork.LocalNetworks.ToIpRanges())
                .Exclude(IpNetwork.MulticastNetworks.ToIpRanges())
                .Exclude(IPAddress.Broadcast);
        }

        return new VpnKillSwitchRules {
            AdapterName = adapterName,
            PassIpRanges = includeIpRanges.Invert(),
            DnsServers = []
        };
    }
}
