using Microsoft.Extensions.Options;
using VpnHood.Common.IpLocations;
using VpnHood.Common.IpLocations.Providers;

namespace VpnHood.AccessServer.Agent.Services;

public class AgentIpLocationProvider(
    IHttpClientFactory clientFactory, 
    ILogger<AgentIpLocationProvider> logger,
    IOptions<AgentOptions> options)
    : CompositeIpLocationProvider(logger, CreateLoggers(clientFactory, options))
{
    private static IIpLocationProvider[] CreateLoggers(IHttpClientFactory clientFactory, IOptions<AgentOptions> options)
    {
        const string userAgent = "VpnHood-AccessManager";
        var httpClient = clientFactory.CreateClient(AgentOptions.HttpClientNameIpLocation);
        var providers = new IIpLocationProvider[] {
            new IpInfoIoProvider(httpClient, userAgent, options.Value.IpInfoIoApiKey),
            new IpLocationIoProvider(httpClient, userAgent, options.Value.IpLocationIoApiKey),
            new IpApiCoLocationProvider(httpClient, userAgent)
        };
        return providers;
    }
}