using System.Net;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Options;
using VpnHood.Common.IpLocations;
using VpnHood.Common.IpLocations.Providers;

namespace VpnHood.AccessServer.Services;

public class ServerIpLocationProvider(
    HttpClient httpClient,
    ILogger<ServerIpLocationProvider> logger,
    IOptions<AppOptions> options)
    : CompositeIpLocationProvider(logger, CreateLoggers(httpClient, options))
{
    private static IIpLocationProvider[] CreateLoggers(HttpClient httpClient, IOptions<AppOptions> options)
    {
        const string userAgent = "VpnHood-Manager";
        var providers = new IIpLocationProvider[] {
            new IpLocationIoProvider(httpClient, userAgent, options.Value.IpLocationIoApiKey),
            new IpInfoIoProvider(httpClient, userAgent, options.Value.IpInfoIoApiKey), // has diacritics in region
            new IpApiCoLocationProvider(httpClient, userAgent)
        };
        return providers;
    }

    public async Task<IpLocation?> TryGetIpLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        try {
            var ipLocation = await GetLocation(ipAddress, cancellationToken);
            return ipLocation;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error while getting location of the ip. Ip: {Ip}", ipAddress);
            return null;
        }
    }

}