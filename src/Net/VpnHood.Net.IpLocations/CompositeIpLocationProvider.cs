using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.IpLocations;

/// <summary>
/// Provides locations from the first of several providers that finds one.
/// </summary>
/// <remarks>
/// The providers are tried in order. Each gets a token canceled after its timeout, and the next is tried only once it
/// ends, so the timeout holds only for a provider that observes its token. A failure is logged, except
/// <see cref="NotSupportedException"/>, and the next provider is tried. Disposing this provider disposes them all.
/// </remarks>
/// <param name="logger">The logger the failures go to.</param>
/// <param name="providers">The providers, in the order they are tried; this provider owns them.</param>
/// <param name="providerTimeout">
/// How long each provider may take before its token is canceled, or <see langword="null"/> for no limit on
/// <see cref="GetLocation"/> and 5 seconds on <see cref="GetCurrentLocation"/>.
/// </param>
public class CompositeIpLocationProvider(
    ILogger logger,
    IEnumerable<IIpLocationProvider> providers,
    TimeSpan? providerTimeout = null)
    : IIpLocationProvider
{
    private readonly CompositeCurrentIpLocationProvider _currentIpLocationProviders = 
        new(logger, providers, providerTimeout);
    
    /// <inheritdoc/>
    /// <exception cref="Exception">No provider found the location.</exception>
    public async Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
            try {
                using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (providerTimeout.HasValue)
                    linkedToken.CancelAfter(providerTimeout.Value);

                return await provider.GetLocation(ipAddress, linkedToken.Token).Vhc();
            }
            catch (NotSupportedException) {
                // no log
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to get location. Provider: {Provider}.", provider.GetType().Name);
            }

        throw new Exception("No location provider could resolve the IP address.");
    }

    /// <inheritdoc/>
    /// <exception cref="KeyNotFoundException">No provider found the location.</exception>
    public Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        return _currentIpLocationProviders.GetCurrentLocation(cancellationToken);
    }

    /// <summary>Disposes every provider, in order; a provider that throws stops the rest.</summary>
    public void Dispose()
    {
        _currentIpLocationProviders.Dispose();
    }
}