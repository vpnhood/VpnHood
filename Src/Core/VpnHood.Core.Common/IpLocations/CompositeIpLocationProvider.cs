using System.Net;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Common.IpLocations;

public class CompositeIpLocationProvider(
    ILogger logger,
    IEnumerable<IIpLocationProvider> providers,
    TimeSpan? providerTimeout = null)
    : IIpLocationProvider
{
    public async Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
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

        throw new KeyNotFoundException("No location provider could resolve the IP address.");
    }

    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
            try {
                using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (providerTimeout.HasValue)
                    linkedToken.CancelAfter(providerTimeout.Value);

                return await provider.GetCurrentLocation(linkedToken.Token);
            }
            catch (NotSupportedException) {
                // no log
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to get current location. Provider: {Provider}.", provider.GetType().Name);
            }

        throw new KeyNotFoundException("No location provider could resolve the current IP address.");
    }
}