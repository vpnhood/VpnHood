using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.IpLocations;

/// <summary>
/// Provides the current location from the first of several providers that finds it.
/// </summary>
/// <remarks>
/// The providers are tried in order. Each gets a token canceled after its timeout, and the next is tried only once it
/// ends, so the timeout holds only for a provider that observes its token. A failure is logged, except
/// <see cref="NotSupportedException"/>, and the next provider is tried. Disposing this provider disposes them all.
/// </remarks>
/// <param name="logger">The logger the failures go to.</param>
/// <param name="providers">The providers, in the order they are tried; this provider owns them.</param>
/// <param name="providerTimeout">
/// How long each provider may take before its token is canceled, or <see langword="null"/> for 5 seconds.
/// </param>
public class CompositeCurrentIpLocationProvider(
    ILogger logger,
    IEnumerable<ICurrentIpLocationProvider> providers,
    TimeSpan? providerTimeout = null)
    : ICurrentIpLocationProvider
{
    /// <inheritdoc/>
    /// <exception cref="KeyNotFoundException">No provider found the location.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
            try {
                using var providerTimeoutCts = new CancellationTokenSource(providerTimeout ?? TimeSpan.FromSeconds(5));
                using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, providerTimeoutCts.Token);
                return await provider.GetCurrentLocation(linkedToken.Token).Vhc();
            }
            catch (NotSupportedException) {
                // no log
            }
            // the caller's cancellation, not this provider's timeout: no other provider is tried
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to get current location. Provider: {Provider}.", provider.GetType().Name);
            }

        throw new KeyNotFoundException("No location provider could resolve the current IP address.");
    }

    /// <summary>Disposes every provider, in order; a provider that throws stops the rest.</summary>
    public void Dispose()
    {
        foreach (var provider in providers)
            provider.Dispose();
    }
}