using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// Provides several providers read as one: an asset is asked of each in turn, and the first that has it answers.
/// </summary>
/// <remarks>
/// What an app lays over a shared store, such as its own logo, shadows the store's file at the same path, and
/// everything else still comes from the store. Only what a provider classes as absent falls through: a file it finds and
/// cannot open fails where it is, so a broken override is not silently replaced by the default.
/// </remarks>
public class CompositeAssetProvider : IAssetProvider
{
    private readonly IReadOnlyList<IAssetProvider> _providers;

    /// <summary>Initializes a new instance of the <see cref="CompositeAssetProvider"/> class.</summary>
    /// <param name="providers">The providers, in the order they are asked.</param>
    /// <exception cref="ArgumentException"><paramref name="providers"/> is empty.</exception>
    public CompositeAssetProvider(IReadOnlyList<IAssetProvider> providers)
    {
        if (providers.Count == 0)
            throw new ArgumentException("A composite asset provider needs at least one provider.", nameof(providers));

        _providers = providers;
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
    {
        foreach (var provider in _providers) {
            var stream = await provider.TryOpenReadAsync(assetPath, cancellationToken).Vhc();
            if (stream != null)
                return stream;
        }

        throw new AssetNotFoundException(assetPath);
    }
}
