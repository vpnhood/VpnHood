using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.Abstractions.Exceptions;

public static class EndPointResolver
{
    private static async Task<IEnumerable<IPEndPoint>> TryGetEndPointFromDns(ServerToken serverToken, CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogInformation("Resolving IP from host name: {HostName}...", VhLogger.FormatHostName(serverToken.HostName));
            var hostEntities = await Dns.GetHostEntryAsync(serverToken.HostName, cancellationToken).Vhc();

            return hostEntities.AddressList
                .Select(x => new IPEndPoint(x, serverToken.HostPort));
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not resolve IpAddress from hostname!");
            return [];
        }
    }

    public static async Task<IPEndPoint[]> ResolveHostEndPoints(ServerToken serverToken,
        EndPointStrategy strategy, CancellationToken cancellationToken)
    {
        // it is not valid host name, so return the endpoints directly
        // if token does not have any endpoints nor host name, throw an exception 
        if (!serverToken.IsValidHostName && VhUtils.IsNullOrEmpty(serverToken.HostEndPoints))
            throw new InvalidOperationException("The token does not contain any server endpoints or a valid hostname. Please contact your provider’s support.");

        // fix token only strategy
        if (strategy == EndPointStrategy.TokenOnly) {
            if (!VhUtils.IsNullOrEmpty(serverToken.HostEndPoints))
                return serverToken.HostEndPoints;

            VhLogger.Instance.LogWarning("TokenOnly strategy is not supported by this token because there are no endpoints in the token. Fallback to DnsOnly.");
            strategy = EndPointStrategy.DnsOnly;
        }

        // fix dns only strategy
        if (strategy == EndPointStrategy.DnsOnly && !serverToken.IsValidHostName) {
            VhLogger.Instance.LogWarning("DnsOnly strategy is not supported by this token because there are valid domain in the token. Fallback to auto.");
            strategy = EndPointStrategy.Auto;
        }

        // if token has host name, try to resolve it
        var dnsEndPoints = await TryGetEndPointFromDns(serverToken, cancellationToken).Vhc();
        var tokenEndPoints = serverToken.HostEndPoints ?? [];

        // follow the end point strategy to combine the endpoints
        var ipEndPoints = strategy switch {
            EndPointStrategy.DnsFirst => dnsEndPoints.Concat(tokenEndPoints),
            EndPointStrategy.TokenFirst => tokenEndPoints.Concat(dnsEndPoints),
            EndPointStrategy.DnsOnly => dnsEndPoints,
            _ => dnsEndPoints.Concat(tokenEndPoints)
        };

        // remove duplicates and return
        var ret = ipEndPoints.Distinct().ToArray();
        if (!ret.Any())
            throw new EndPointDiscoveryException("Could not resolve any host endpoint from AccessToken.");

        return ret;
    }
}