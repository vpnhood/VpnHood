using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Client;

public class ServerTokenHelper
{
    private static async Task<IPEndPoint[]> ResolveHostEndPointsInternal(ServerToken serverToken,
        CancellationToken cancellationToken)
    {
        if (serverToken.IsValidHostName) {
            try {
                VhLogger.Instance.LogInformation("Resolving IP from host name: {HostName}...",
                    VhLogger.FormatHostName(serverToken.HostName));

                var hostEntities = await Dns.GetHostEntryAsync(serverToken.HostName)
                    .VhWait(cancellationToken)
                    .VhConfigureAwait();

                if (!VhUtil.IsNullOrEmpty(hostEntities.AddressList)) {
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

        if (!VhUtil.IsNullOrEmpty(serverToken.HostEndPoints))
            return serverToken.HostEndPoints;

        throw new Exception($"Could not resolve {nameof(serverToken.HostEndPoints)} from token!");
    }

    public static async Task<IPEndPoint[]> ResolveHostEndPoints(ServerToken serverToken,
        CancellationToken cancellationToken)
    {
        var ipEndPoints = await ResolveHostEndPointsInternal(serverToken, cancellationToken).VhConfigureAwait();
        if (VhUtil.IsNullOrEmpty(ipEndPoints))
            throw new Exception("Could not resolve any host endpoint from AccessToken.");

        return ipEndPoints;
    }
}