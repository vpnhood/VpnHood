using System.Net;
using Microsoft.Extensions.Logging;

namespace VpnHood.Common.IpLocations.Providers;

public class CompositeIpLocationProvider(ILogger logger, IIpLocationProvider[] providers, TimeSpan? timeout = null) : IIpLocationProvider
{
    public async Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        foreach (var provider in providers) {
            try {
                // create timeout token
                using var timeoutToken = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
                using var linkedToken =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutToken.Token);
                return await provider.GetLocation(ipAddress, linkedToken.Token);
            }
            catch (NotSupportedException) {
                // no log
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to get location. Provider: {Provider}.", provider.GetType().Name);
            }
        }

        throw new Exception("No location provider could resolve the IP address.");
    }

    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        foreach (var provider in providers) {
            try {
                // create timeout token
                using var timeoutToken = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
                using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutToken.Token);
                return await provider.GetCurrentLocation(linkedToken.Token);
            }
            catch (NotSupportedException) {
                // no log
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to get current location. Provider: {Provider}.", provider.GetType().Name);
            }
        }

        throw new Exception("No location provider could resolve the current IP address.");
    }
}