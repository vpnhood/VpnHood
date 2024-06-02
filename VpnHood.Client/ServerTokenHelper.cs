using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

namespace VpnHood.Client;

public class ServerTokenHelper
{
    private static async Task<IPEndPoint[]> ResolveHostEndPointsInternal(ServerToken serverToken)
    {
        if (serverToken.IsValidHostName)
        {
            try
            {
                VhLogger.Instance.LogInformation("Resolving IP from host name: {HostName}...", VhLogger.FormatHostName(serverToken.HostName));
                var hostEntities = await Dns.GetHostEntryAsync(serverToken.HostName).VhConfigureAwait();
                if (!VhUtil.IsNullOrEmpty(hostEntities.AddressList))
                {
                    return hostEntities.AddressList
                        .Select(x => new IPEndPoint(x, serverToken.HostPort))
                        .ToArray();
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not resolve IpAddress from hostname!");
            }
        }

        if (!VhUtil.IsNullOrEmpty(serverToken.HostEndPoints))
            return serverToken.HostEndPoints;

        throw new Exception($"Could not resolve {nameof(serverToken.HostEndPoints)} from token!");
    }

    public static async Task<IPEndPoint[]> ResolveHostEndPoints(ServerToken serverToken)
    {
        var endPoints = await ResolveHostEndPointsInternal(serverToken).VhConfigureAwait();
        if (VhUtil.IsNullOrEmpty(endPoints))
            throw new Exception("Could not resolve any host endpoint from AccessToken!");

        var ipV4EndPoints = endPoints.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
        var ipV6EndPoints = endPoints.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();

        if (ipV6EndPoints.Length == 0) return ipV4EndPoints;
        if (ipV4EndPoints.Length == 0) return ipV6EndPoints;
        var publicAddressesIpV6 = await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6).VhConfigureAwait();
        return publicAddressesIpV6 != null ? ipV6EndPoints : ipV4EndPoints; //return IPv6 if user has access to IpV6
    }

    public static async Task<IPEndPoint> ResolveHostEndPoint(ServerToken serverToken)
    {
        var endPoints = await ResolveHostEndPoints(serverToken).VhConfigureAwait();
        if (VhUtil.IsNullOrEmpty(endPoints))
            throw new Exception("Could not resolve any host endpoint!");

        var rand = new Random();
        return endPoints[rand.Next(0, endPoints.Length)];
    }
}