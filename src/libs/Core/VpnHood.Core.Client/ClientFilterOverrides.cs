using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Client;

// What the client does past the filters' verdict, the same for a packet (ClientPacketHandler) and for a TCP connection
// that the TCP host decides as a stream (ClientStreamHandler), which decides again whatever the packet path chose for
// its packets: kept apart, the stream path let a blocked host out with an ad's passthrough, and DNS out by an exclude.
internal static class ClientFilterOverrides
{
    public static FilterAction Apply(FilterAction filterAction, IpEndPointValue destination, bool passthroughForAd,
        IpAddressValue[] dnsServers, SplitDnsMode splitDnsMode)
    {
        // an address the user chose to drop stays dropped, whatever follows
        if (filterAction == FilterAction.Block)
            return filterAction;

        // While an ad's passthrough is on, the ad's traffic goes around the tunnel, so the ad provider sees no VPN. DNS
        // on any transport (udp/tcp 53, DoT, DoQ), and anything to a configured resolver, is no ad traffic: it stays,
        // so the names resolve and regional ad blockers are passed by.
        if (passthroughForAd && !DnsPorts.IsDnsPort(destination.Port) && !dnsServers.Contains(destination.AddressValue))
            filterAction = FilterAction.Exclude;

        // Keep DNS inside the tunnel (SplitDnsMode): a resolver reachable around the VPN leaks every name the user looks
        // up and lets the local network answer for them, so DNS overrides any split that excluded it, and the ad's
        // passthrough, which can never undo it. Detection is by destination port, the only DNS signal at this level:
        // 53 is DNS over both UDP and TCP, 853 is DoT; DoH, on 443, is any other HTTPS flow. No routability check: a
        // destination the server does not route is the server's to drop, a visible failure, and the query never
        // travels outside. No LAN exception: whether the local network is captured at all is the device include set's
        // business.
        if (splitDnsMode is SplitDnsMode.IncludeAll && DnsPorts.IsDnsPort(destination.Port))
            filterAction = FilterAction.Include;

        return filterAction;
    }
}
