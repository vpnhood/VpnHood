using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Provides the resolution of a server token's endpoints: its host name's addresses and those it lists, in the order an
/// <see cref="EndPointStrategy"/> says.
/// </summary>
public static class EndPointResolver
{
    private static readonly TimeSpan OwnResolverTimeout = TimeSpan.FromSeconds(5);

    private static async Task<IEnumerable<IPEndPoint>> TryGetEndPointFromDns(ServerToken serverToken,
        ISocketFactory? ownResolverSocketFactory, HostNameAddresses? knownAddresses,
        CancellationToken cancellationToken)
    {
        if (knownAddresses != null &&
            string.Equals(knownAddresses.HostName, serverToken.HostName, StringComparison.OrdinalIgnoreCase)) {
            VhLogger.Instance.LogInformation("Taking the host name's addresses known already: {HostName}",
                VhLogger.FormatHostName(serverToken.HostName));
            return knownAddresses.Addresses.Select(x => new IPEndPoint(x, serverToken.HostPort));
        }

        try {
            VhLogger.Instance.LogInformation("Resolving IP from host name: {HostName}...",
                VhLogger.FormatHostName(serverToken.HostName));
            var addresses = ownResolverSocketFactory != null
                ? await ResolveByOwnResolver(serverToken.HostName, ownResolverSocketFactory, cancellationToken).Vhc()
                : (await Dns.GetHostEntryAsync(serverToken.HostName, cancellationToken).Vhc()).AddressList;

            return addresses.Select(x => new IPEndPoint(x, serverToken.HostPort));
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not resolve IpAddress from hostname!");
            return [];
        }
    }

    // The name asked of the network's resolvers from this process, over sockets the factory makes: under the
    // kill switch's block, which lets only the VPN service's own traffic out, the OS's resolver cannot answer,
    // as it asks from a process of its own (Windows' DNS Client, systemd-resolved). Keep no copy of a name's last
    // addresses for it: a crash's redial tries the dead session's server first (ConnectedServerFile), a connect's
    // token update brings the server's addresses, and what is left - a farm that gives names only, on a box whose
    // only resolver is a stub other than systemd-resolved - is where a kept address is likeliest stale.
    private static async Task<IPAddress[]> ResolveByOwnResolver(string hostName, ISocketFactory socketFactory,
        CancellationToken cancellationToken)
    {
        var dnsServers = DnsUtils.GetNetworkDnsServers();
        foreach (var dnsServer in dnsServers) {
            try {
                using var udpClient = socketFactory.CreateUdpClient(dnsServer.AddressFamily);
                var hostEntry = await DnsUtils.GetHostEntry(hostName, new IPEndPoint(dnsServer, 53), udpClient,
                    OwnResolverTimeout, cancellationToken).Vhc();
                if (hostEntry.AddressList.Length > 0)
                    return hostEntry.AddressList;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "A network's resolver could not resolve the server's name. DnsServer: {DnsServer}",
                    VhLogger.Format(dnsServer));
            }
        }

        throw new InvalidOperationException(
            $"The network's resolvers could not resolve the server's name. DnsServers: {dnsServers.Count}");
    }

    /// <summary>Resolves a server token's endpoints, as <see cref="ResolveHostEndPointsAndName"/> does.</summary>
    /// <param name="serverToken">The server token.</param>
    /// <param name="strategy">The order, or <see cref="EndPointStrategy.Auto"/> for the token's own.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <param name="ownResolverSocketFactory">
    /// The factory of sockets to ask the network's resolvers from this process, as where the kill switch's block lets
    /// only the VPN service's traffic out, or <see langword="null"/> for the system's resolver. The network's resolvers
    /// are asked in turn, over UDP and for IPv4 addresses only, five seconds each, and the first answer is taken; the
    /// system's resolver is not asked then.
    /// </param>
    /// <returns>
    /// A task whose result is the endpoints, without repeats but for <see cref="EndPointStrategy.IpOnly"/>'s, which are
    /// the token's list as it is.
    /// </returns>
    /// <exception cref="InvalidOperationException">The token has neither endpoints nor a real host name.</exception>
    /// <exception cref="Exceptions.EndPointDiscoveryException">No endpoint was found.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<IPEndPoint[]> ResolveHostEndPoints(ServerToken serverToken,
        EndPointStrategy strategy, CancellationToken cancellationToken, ISocketFactory? ownResolverSocketFactory = null)
    {
        var resolved = await ResolveHostEndPointsAndName(serverToken, strategy, cancellationToken,
            ownResolverSocketFactory).Vhc();
        return resolved.EndPoints.ToArray();
    }

    /// <summary>
    /// Resolves a server token's endpoints: its host name's addresses, where it is a real one, and those it lists,
    /// ordered as the strategy says.
    /// </summary>
    /// <param name="serverToken">The server token.</param>
    /// <param name="strategy">
    /// The order, or <see cref="EndPointStrategy.Auto"/> for the token's own. <see cref="EndPointStrategy.IpOnly"/> with
    /// no endpoints listed falls back to <see cref="EndPointStrategy.DnsOnly"/>, and that with no real host name to
    /// <see cref="EndPointStrategy.Auto"/>.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <param name="ownResolverSocketFactory">
    /// The factory of sockets to ask the network's resolvers from this process, as where the kill switch's block lets
    /// only the VPN service's traffic out, or <see langword="null"/> for the system's resolver. The network's resolvers
    /// are asked in turn, over UDP and for IPv4 addresses only, five seconds each, and the first answer is taken; the
    /// system's resolver is not asked then.
    /// </param>
    /// <param name="knownAddresses">
    /// The host name's addresses, known already, which are taken rather than asking the name where theirs is the
    /// token's, in any letter case, or <see langword="null"/>.
    /// </param>
    /// <returns>A task whose result is the endpoints, with those the name gave among them.</returns>
    /// <remarks>
    /// A failure to resolve the name is logged and leaves the listed endpoints, or, under
    /// <see cref="EndPointStrategy.DnsOnly"/>, none, which fails. <see cref="EndPointStrategy.IpOnly"/>'s endpoints are
    /// the token's list as it is; the others come without repeats.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The token has neither endpoints nor a real host name.</exception>
    /// <exception cref="Exceptions.EndPointDiscoveryException">No endpoint was found.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<ResolvedHostEndPoints> ResolveHostEndPointsAndName(ServerToken serverToken,
        EndPointStrategy strategy, CancellationToken cancellationToken, ISocketFactory? ownResolverSocketFactory = null,
        HostNameAddresses? knownAddresses = null)
    {
        // use token strategy if auto
        if (strategy == EndPointStrategy.Auto)
            strategy = serverToken.EndPointsStrategy;

        // it is not valid host name, so return the endpoints directly
        // if token does not have any endpoints nor host name, throw an exception 
        if (!serverToken.IsValidHostName && VhUtils.IsNullOrEmpty(serverToken.HostEndPoints))
            throw new InvalidOperationException(
                "The token does not contain any server endpoints or a valid hostname. Please contact your provider's support.");

        // fix token only strategy
        if (strategy == EndPointStrategy.IpOnly) {
            if (!VhUtils.IsNullOrEmpty(serverToken.HostEndPoints))
                return new ResolvedHostEndPoints { EndPoints = serverToken.HostEndPoints, NameEndPoints = [] };

            VhLogger.Instance.LogWarning(
                "TokenOnly strategy is not supported by this token because there are no endpoints in the token. Fallback to DnsOnly.");
            strategy = EndPointStrategy.DnsOnly;
        }

        // fix dns only strategy
        if (strategy == EndPointStrategy.DnsOnly && !serverToken.IsValidHostName) {
            VhLogger.Instance.LogWarning(
                "DnsOnly strategy is not supported by this token because there are valid domain in the token. Fallback to auto.");
            strategy = EndPointStrategy.Auto;
        }

        // if token has host name, try to resolve it
        var tokenEndPoints = serverToken.HostEndPoints ?? [];
        var dnsEndPoints = serverToken.IsValidHostName
            ? await TryGetEndPointFromDns(serverToken, ownResolverSocketFactory, knownAddresses, cancellationToken).Vhc()
            : [];

        // follow the end point strategy to combine the endpoints
        var ipEndPoints = strategy switch {
            EndPointStrategy.DnsFirst => dnsEndPoints.Concat(tokenEndPoints),
            EndPointStrategy.IpFirst => tokenEndPoints.Concat(dnsEndPoints),
            EndPointStrategy.DnsOnly => dnsEndPoints,
            _ => dnsEndPoints.Concat(tokenEndPoints)
        };

        // remove duplicates and return
        var ret = ipEndPoints.Distinct().ToArray();
        if (!ret.Any())
            throw new EndPointDiscoveryException("Could not resolve any host endpoint from AccessToken.");

        return new ResolvedHostEndPoints {
            EndPoints = ret,
            NameEndPoints = dnsEndPoints.ToArray()
        };
    }
}