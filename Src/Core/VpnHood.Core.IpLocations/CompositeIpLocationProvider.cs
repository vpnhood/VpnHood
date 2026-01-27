using System.Net;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.IpLocations;

public class CompositeIpLocationProvider(
    ILogger logger,
    IEnumerable<IIpLocationProvider> providers,
    TimeSpan? providerTimeout = null)
    : IIpLocationProvider
{
    CompositeCurrentIpLocationProvider _currentIpLocationProviders = 
        new(logger, providers, providerTimeout);
    
    public async Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        foreach (var provider in providers.Where(x=>x is IIpLocationProvider))
            try {
                using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (providerTimeout.HasValue)
                    linkedToken.CancelAfter(providerTimeout.Value);

                return await provider.GetLocation(ipAddress, linkedToken.Token);
            }
            catch (NotSupportedException) {
                // no log
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to get location. Provider: {Provider}.", provider.GetType().Name);
            }

        throw new Exception("No location provider could resolve the IP address.");
    }

    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        return await _currentIpLocationProviders.GetCurrentLocation(cancellationToken);
    }

    public void Dispose()
    {
        _currentIpLocationProviders.Dispose();
    }
}