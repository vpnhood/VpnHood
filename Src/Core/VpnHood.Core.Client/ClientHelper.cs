using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
// ReSharper disable PossibleMultipleEnumeration

namespace VpnHood.Core.Client;

internal static class ClientHelper
{
    private static bool IsIncluded(IIpFilter clientIpFilter, IPAddress ipAddress)
    {
        return clientIpFilter.Process(IpProtocol.Udp, new IpEndPointValue(ipAddress, 53)) is FilterAction.Include;
    }

    /// <summary>
    /// Selects appropriate DNS servers based on user preferences, server configuration, and routing rules.
    /// </summary>
    /// <param name="userDnsAddresses">DNS servers specified by the user</param>
    /// <param name="serverDnsAddresses">DNS servers provided by the VPN server</param>
    /// <param name="serverIncludeIpRanges">IP ranges that the server routes through the tunnel</param>
    /// <param name="clientIpFilter">IP filter to determine if a DNS server is routable by the client</param>
    /// <returns>Selected DNS server addresses</returns>
    public static DnsStatus GetDnsServers(
        IReadOnlyList<IPAddress>? userDnsAddresses,
        IReadOnlyList<IPAddress> serverDnsAddresses,
        IpRangeOrderedList serverIncludeIpRanges,
        IIpFilter clientIpFilter)
    {
        IEnumerable<IPAddress>? results;
        var isUserSuppressed = false;

        // Try to use user DNS servers
        if (userDnsAddresses?.Any() == true) {
            // Use user DNS servers if they explicitly excluded by filters 
            results = userDnsAddresses.Where(x => !IsIncluded(clientIpFilter, x));
            if (results.Any()) {
                VhLogger.Instance.LogInformation(
                    "Using User's DNS servers, but they are not excluded from VPN because of IP filters. DnsServers: {DnsServers}",
                    VhLogger.Format(results));
                return new DnsStatus {
                    DnsServers = results.ToArray(),
                    IsIncludedInVpn = false,
                    IsUserSuppressed = isUserSuppressed, // user override is false because they are not explicitly excluded by filters, but just not included in server routing
                    DnsSelection = DnsSelection.UserDns
                };
            }

            // Use user DNS servers if they are routable by the server
            results = userDnsAddresses.Where(serverIncludeIpRanges.Contains);
            if (results.Any()) {
                VhLogger.Instance.LogInformation(
                    "Using User's DNS servers. DnsServers: {DnsServers}",
                    VhLogger.Format(results));

                return new DnsStatus {
                    DnsServers = results.ToArray(),
                    IsIncludedInVpn = true,
                    IsUserSuppressed = isUserSuppressed,
                    DnsSelection = DnsSelection.UserDns
                };
            }

            // Log warning because user DNS servers are not routable
            isUserSuppressed = true;
            VhLogger.Instance.LogWarning(
                "Client DNS servers have been ignored because the server does not route them.");
        }

        // Use server default DNS servers if they are routable by the client
        if (serverDnsAddresses.Any()) {
            results = serverDnsAddresses.Where(x => IsIncluded(clientIpFilter, x));
            if (results.Any()) {
                VhLogger.Instance.LogInformation(
                    "Using Server default DNS servers. DnsServers: {DnsServers}",
                    VhLogger.Format(results));

                return new DnsStatus {
                    DnsServers = results.ToArray(),
                    IsIncludedInVpn = true,
                    IsUserSuppressed = isUserSuppressed,
                    DnsSelection = DnsSelection.ServerDns
                };
            }
        }

        // Use Google DNS as last resort if they are routable by both client and server
        results = IPAddressUtil.GoogleDnsServers
            .Where(x => IsIncluded(clientIpFilter, x))
            .Where(serverIncludeIpRanges.Contains);
        if (results.Any()) {
            VhLogger.Instance.LogInformation(
                "Using Google DNS servers as default. DnsServers: {DnsServers}",
                VhLogger.Format(results));

            return new DnsStatus {
                DnsServers = results.ToArray(),
                IsIncludedInVpn = true,
                IsUserSuppressed = isUserSuppressed,
                DnsSelection = DnsSelection.GoogleDns
            };
        }

        // Fallback: use Google DNS even if not routable
        results = IPAddressUtil.GoogleDnsServers;
        VhLogger.Instance.LogWarning(
            "Using Google DNS servers, but they are not excluded from VPN because of IP filters. DnsServers: {DnsServers}",
            VhLogger.Format(results));
        return new DnsStatus {
            DnsServers = results.ToArray(),
            IsIncludedInVpn = false,
            IsUserSuppressed = isUserSuppressed,
            DnsSelection = DnsSelection.GoogleDns
        };
    }

    public static IpRangeOrderedList BuildIncludeIpRangesByDevice(
        IpRangeOrderedList includeIpRanges,
        IReadOnlyList<IPAddress> catcherIps,
        bool canProtectSocket,
        bool includeLocalNetwork,
        IPAddress hostIpAddress)
    {
        // exclude server if ProtectClient is not supported to prevent loop
        if (!canProtectSocket)
            includeIpRanges = includeIpRanges.Exclude(hostIpAddress);

        // exclude local networks
        if (!includeLocalNetwork) {
            includeIpRanges = includeIpRanges
                .Exclude(IpNetwork.LocalNetworks.ToIpRanges())
                .Exclude(IpNetwork.MulticastNetworks.ToIpRanges())
                .Exclude(IPAddress.Broadcast);
        }

        // Make sure CatcherAddress is included
        includeIpRanges = includeIpRanges.Union(catcherIps.ToIpRanges());

        return includeIpRanges; //sort and unify
    }

}
