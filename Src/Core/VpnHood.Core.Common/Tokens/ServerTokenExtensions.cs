using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Common.Tokens;

public static class ServerTokenExtensions
{
    private static async Task<IPEndPoint[]> ResolveHostEndPointsInternal(this ServerToken serverToken,
        CancellationToken cancellationToken)
    {
        if (serverToken.IsValidHostName) {
            try {
                VhLogger.Instance.LogInformation("Resolving IP from host name: {HostName}...",
                    VhLogger.FormatHostName(serverToken.HostName));

                var hostEntities = await Dns.GetHostEntryAsync(serverToken.HostName)
                    .VhWait(cancellationToken)
                    .VhConfigureAwait();

                if (!VhUtils.IsNullOrEmpty(hostEntities.AddressList)) {
                    return hostEntities.AddressList
                        .Select(x => new IPEndPoint(x, serverToken.HostPort))
                        .ToArray();
                }
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not resolve IpAddress from hostname!");
            }
        }

        if (!VhUtils.IsNullOrEmpty(serverToken.HostEndPoints))
            return serverToken.HostEndPoints;

        throw new Exception($"Could not resolve {nameof(serverToken.HostEndPoints)} from token!");
    }

    public static async Task<IPEndPoint[]> ResolveHostEndPoints(this ServerToken serverToken,
        CancellationToken cancellationToken)
    {
        var ipEndPoints = await ResolveHostEndPointsInternal(serverToken, cancellationToken).VhConfigureAwait();
        if (VhUtils.IsNullOrEmpty(ipEndPoints))
            throw new Exception("Could not resolve any host endpoint from AccessToken.");

        return ipEndPoints;
    }

    public static bool IsTokenUpdated(this ServerToken serverToken, ServerToken newServerToken)
    {
        // create first server token by removing its created time
        var serverToken1 = JsonUtils.JsonClone(serverToken);
        serverToken1.CreatedTime = DateTime.MinValue;

        // create second server token by removing its created time
        var serverToken2 = JsonUtils.JsonClone(newServerToken);
        serverToken2.CreatedTime = DateTime.MinValue;

        // compare
        if (JsonSerializer.Serialize(serverToken1) == JsonSerializer.Serialize(serverToken2))
            return false;

        // if token are not equal, check if new token CreatedTime is newer or equal.
        // If created time is equal assume new token is updated because there is change in token.
        return newServerToken.CreatedTime >= serverToken.CreatedTime;
    }
}