using System.Net;
using Microsoft.Extensions.Logging;

namespace VpnHood.Common.IpLocations.Providers;

public class CompositeIpLocationProvider(ILogger logger, IIpLocationProvider[] providers) : IIpLocationProvider
{
    public async Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        foreach (var provider in providers) {
            try {
                return await provider.GetLocation(ipAddress, cancellationToken);
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
                return await provider.GetCurrentLocation(cancellationToken);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to get current location. Provider: {Provider}.", provider.GetType().Name);
            }
        }

        throw new Exception("No location provider could resolve the IP address.");
    }
}