using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;
// ReSharper disable PossibleMultipleEnumeration

namespace VpnHood.Core.Client;

internal static class ClientHelper
{
    /// <summary>
    /// Wraps the caller's factory into the one the client and its services must use. Protecting the socket
    /// is mandatory: an unprotected socket is captured by our own adapter, so traffic to the server or to
    /// the proxies would loop back into the tunnel it is trying to establish.
    /// </summary>
    public static ConfiguringSocketFactory CreateSocketFactory(ISocketFactory socketFactory,
        IVpnAdapter vpnAdapter, ClientOptions options)
    {
        if (vpnAdapter.CanProtectSocket)
            socketFactory = new AdapterSocketFactory(socketFactory, vpnAdapter);

        return new ConfiguringSocketFactory(new BindingSocketFactory(socketFactory)) {
            KeepAlive = true,
            NoDelay = true,
            TcpKernelBufferSize = options.TcpKernelBufferSize
        };
    }

    private static bool IsIncluded(IIpFilter clientIpFilter, IPAddress ipAddress)
    {
        // tunnel when no gate vetoed (Default) or an override forced it (Include)
        return clientIpFilter.Process(IpProtocol.Udp, new IpEndPointValue(ipAddress, 53))
            is FilterAction.Default or FilterAction.Include;
    }

    /// <summary>
    /// Selects appropriate DNS servers based on user preferences, server configuration, and routing rules.
    /// </summary>
    /// <param name="userDnsAddresses">DNS servers specified by the user</param>
    /// <param name="serverDnsAddresses">DNS servers provided by the VPN server</param>
    /// <param name="serverIncludeIpRanges">IP ranges the session can route through the tunnel: the server's full routing declaration (app ∩ adapter ranges), which by default excludes local networks</param>
    /// <param name="ipFilter">IP filter to determine if a DNS server is routable by the client</param>
    /// <param name="splitDnsMode">IncludeAll refuses an out-of-tunnel resolver: a user server the splits exclude is not honored, and when nothing is tunnelable at all it throws instead of leaking</param>
    /// <returns>Selected DNS server addresses</returns>
    public static DnsConfig GetDnsServers(
        IReadOnlyList<IPAddress>? userDnsAddresses,
        IReadOnlyList<IPAddress> serverDnsAddresses,
        IpRangeOrderedList serverIncludeIpRanges,
        IIpFilter ipFilter,
        SplitDnsMode splitDnsMode)
    {
        IEnumerable<IPAddress>? results;
        var isUserSuppressed = false;

        // Try to use user DNS servers
        if (userDnsAddresses?.Any() == true) {
            // Servers the session can not carry through the tunnel — vetoed by the IP filters (via-app
            // splits) or not routed by the server (which covers a LAN resolver such as a Pi-hole: default
            // servers do not route local networks) — can only work OUTSIDE it, and the runtime already
            // routes them there (bypassed by the allow set, or simply never captured). Honor that only when
            // SplitDnsMode lets DNS travel outside: IncludeAll keeps every query inside, so these are
            // skipped and selection continues with the tunnelable candidates. The device set is not
            // consulted: an in-tunnel plan is made capturable afterwards by unioning its resolvers into the
            // device set (BuildIncludeIpRangesByDevice).
            results = userDnsAddresses.Where(x => !IsIncluded(ipFilter, x) || !serverIncludeIpRanges.Contains(x));
            if (results.Any()) {
                if (splitDnsMode is SplitDnsMode.IncludeAll) {
                    VhLogger.Instance.LogWarning(
                        "User's DNS servers can not go through the tunnel (excluded by filters or not routed by the server), and SplitDnsMode does not let them be used outside. DnsServers: {DnsServers}",
                        VhLogger.Format(results));
                }
                else {
                    VhLogger.Instance.LogWarning(
                        "Using user's DNS servers outside the tunnel because the session can not tunnel them (excluded by filters or not routed by the server). DnsServers: {DnsServers}",
                        VhLogger.Format(results));
                    return new DnsConfig {
                        DnsServers = results.ToArray(),
                        IsIncludedInVpn = false,
                        IsUserSuppressed = isUserSuppressed, // false: the user's own exclusion is being honored, not suppressed
                        DnsSelection = DnsSelection.UserDns
                    };
                }
            }

            // Use user DNS servers if the session can route them. serverIncludeIpRanges carries the server's
            // FULL routing declaration (app ∩ adapter ranges), which by default already excludes local
            // networks — so a home-LAN resolver is rejected here by the server's own word, no LAN heuristic
            // needed, while a server that deliberately routes its internal ranges keeps them usable.
            results = userDnsAddresses.Where(serverIncludeIpRanges.Contains);
            if (results.Any()) {
                VhLogger.Instance.LogInformation(
                    "Using User's DNS servers. DnsServers: {DnsServers}",
                    VhLogger.Format(results));

                return new DnsConfig {
                    DnsServers = results.ToArray(),
                    IsIncludedInVpn = true,
                    IsUserSuppressed = isUserSuppressed,
                    DnsSelection = DnsSelection.UserDns
                };
            }

            // Only IncludeAll reaches this point: under DefaultRoute every user server is either tunnelable
            // (branch above) or honored outside (first branch) — the two predicates cover everything.
            isUserSuppressed = true;
            VhLogger.Instance.LogWarning(
                "User's DNS servers have been ignored because they can not be tunneled to the VPN server.");
        }

        // Use server default DNS servers if they are routable by the client
        if (serverDnsAddresses.Any()) {
            results = serverDnsAddresses.Where(x => IsIncluded(ipFilter, x));
            if (results.Any()) {
                VhLogger.Instance.LogInformation(
                    "Using Server default DNS servers. DnsServers: {DnsServers}",
                    VhLogger.Format(results));

                return new DnsConfig {
                    DnsServers = results.ToArray(),
                    IsIncludedInVpn = true,
                    IsUserSuppressed = isUserSuppressed,
                    DnsSelection = DnsSelection.ServerDns
                };
            }
        }

        // Use Google DNS as last resort if they are routable by both client and server
        results = IPAddressUtil.GoogleDnsServers
            .Where(x => IsIncluded(ipFilter, x))
            .Where(serverIncludeIpRanges.Contains);
        if (results.Any()) {
            VhLogger.Instance.LogInformation(
                "Using Google DNS servers as default. DnsServers: {DnsServers}",
                VhLogger.Format(results));

            return new DnsConfig {
                DnsServers = results.ToArray(),
                IsIncludedInVpn = true,
                IsUserSuppressed = isUserSuppressed,
                DnsSelection = DnsSelection.GoogleDns
            };
        }

        // Under IncludeAll an out-of-tunnel resolver may never be used, and reaching this point means no
        // in-tunnel candidate exists at all — fail the connect (fail-closed) instead of silently leaking
        // every DNS query around the tunnel. A failed connect is visible; a leak is not.
        if (splitDnsMode is SplitDnsMode.IncludeAll)
            throw new InvalidOperationException(
                "No DNS server can be tunneled to the VPN server while DNS routing keeps DNS inside the tunnel. " +
                "Check the DNS settings or set DNS routing to Default Route.");

        // Fallback: use Google DNS even if not routable
        results = IPAddressUtil.GoogleDnsServers;
        VhLogger.Instance.LogWarning(
            "Using Google DNS servers although the session can not route them through the VPN; DNS may not work. DnsServers: {DnsServers}",
            VhLogger.Format(results));
        return new DnsConfig {
            DnsServers = results.ToArray(),
            IsIncludedInVpn = false,
            IsUserSuppressed = isUserSuppressed,
            DnsSelection = DnsSelection.GoogleDns
        };
    }

    // Builds the adapter's include set — what the device routes into the tunnel. The DNS plan is part of the
    // calculation, not a caller ritual: an in-tunnel DnsConfig is a promise this set must keep (under
    // IncludeAll the packet-level force additionally relies on the capture), so its resolvers are unioned
    // back AFTER the exclusions where no split can strip them, while an out-of-tunnel plan leaves the set
    // untouched — its resolvers stay outside on purpose. The host address is excluded LAST: the tunnel is
    // built over it, and routing it inside itself would loop, even when the user points DNS at the server.
    public static IpRangeOrderedList BuildIncludeIpRangesByDevice(
        IpRangeOrderedList includeIpRanges,
        bool canProtectSocket,
        bool includeLocalNetwork,
        IPAddress hostIpAddress,
        DnsConfig dnsConfig)
    {
        // exclude local networks
        if (!includeLocalNetwork) {
            includeIpRanges = includeIpRanges
                .Exclude(IpNetwork.LocalNetworks.ToIpRanges())
                .Exclude(IpNetwork.MulticastNetworks.ToIpRanges())
                .Exclude(IPAddress.Broadcast);
        }

        // an in-tunnel DNS plan must be capturable
        if (dnsConfig.IsIncludedInVpn)
            includeIpRanges = includeIpRanges.Union(dnsConfig.DnsServers.Select(x => new IpRange(x)));

        // exclude server even if VpnAdapter can protect socket, because we can not protect MS-QUIC sockets
        includeIpRanges = includeIpRanges.Exclude(hostIpAddress);

        return includeIpRanges; //sort and unify
    }
}