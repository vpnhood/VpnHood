using Microsoft.Extensions.Logging;

namespace VpnHood.Core.IpLocations;

public class CompositeCurrentIpLocationProvider(
    ILogger logger,
    IEnumerable<ICurrentIpLocationProvider> providers,
    TimeSpan? providerTimeout = null)
    : ICurrentIpLocationProvider
{
    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
            try {
                using var providerTimeoutCts = new CancellationTokenSource(providerTimeout ?? TimeSpan.FromSeconds(5));
                using var linkedToken =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, providerTimeoutCts.Token);
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

    public void Dispose()
    {
        foreach (var provider in providers)
            provider.Dispose();
    }
}