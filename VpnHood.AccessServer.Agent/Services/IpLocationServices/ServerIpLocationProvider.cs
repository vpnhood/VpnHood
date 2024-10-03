using Microsoft.Extensions.Options;
using VpnHood.Common.IpLocations;
using VpnHood.Common.IpLocations.Providers;

namespace VpnHood.AccessServer.Agent.Services.IpLocationServices;

public class ServerIpLocationProvider(
    IHttpClientFactory clientFactory,
    ILogger<DeviceIpLocationProvider> logger,
    IOptions<AgentOptions> options)
    : CompositeIpLocationProvider(logger, CreateLoggers(clientFactory, options))
{
    private static IIpLocationProvider[] CreateLoggers(IHttpClientFactory clientFactory, IOptions<AgentOptions> options)
    {
        const string userAgent = "VpnHood-AccessManager";
        var httpClient = clientFactory.CreateClient(AgentOptions.HttpClientNameIpLocation);
        var providers = new IIpLocationProvider[] {
            new IpLocationIoProvider(httpClient, userAgent, options.Value.IpLocationIoApiKey),
            new IpInfoIoProvider(httpClient, userAgent, options.Value.IpInfoIoApiKey), // has diacritics in region
            new IpApiCoLocationProvider(httpClient, userAgent)
        };
        return providers;
    }
}