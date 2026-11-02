using System.Net;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

// What a kill switch's block lets out besides the tunnel and the VPN service: the device-level split only,
// what the adapter's routes leave out. The app-level splits (country, IP via app, domain) are the service's:
// it sends their traffic out itself while connected, which the block lets through as the service's own, so
// none of their addresses goes into the block, and while blocked with no tunnel they wait for it (owner,
// 2026-10-31). A country's thousands of ranges would be too many for the OS's firewall anyway.
public class VpnKillSwitchRules
{
    // The tunnel's adapter, by the name its settings give it (ClientOptions.AdapterName): its traffic passes
    // once it is up.
    public required string AdapterName { get; init; }

    // What the adapter's routes leave out: the LAN unless it is included, multicast, broadcast, and the
    // person's IP-via-device list. Not the server's address, which only the service reaches.
    public required IpRangeOrderedList PassIpRanges { get; init; }

    // The resolvers DNS may reach outside the tunnel: an out-of-tunnel plan's. Every other DNS query outside
    // it is blocked, a LAN router's too, which would otherwise learn every name.
    public required IReadOnlyList<IPAddress> DnsServers { get; init; }

    // By the person's settings, with no session: a connect's block until its adapter starts, and the app's own
    // while disconnected with the block kept (UserSettings.KillSwitchLockdown). The server's own exclusions come
    // only with a session, and so does its DNS plan, so no resolver passes.
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
