using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.Core.Toolkit.Assets;

// Several stores read as one: an asset is asked of each provider in turn, and the first that has it
// answers. What a head lays over the UI's own store - its logo, its consent summary, a picture of
// its own - shadows the store's file at the same path, and everything the head did not lay over
// still comes from the store. Only an absent asset falls through: a file that exists and will not
// open fails where it is, so a broken override is never silently replaced by the default.
public class CompositeAssetProvider : IAssetProvider
{
    private readonly IReadOnlyList<IAssetProvider> _providers;

    public CompositeAssetProvider(IReadOnlyList<IAssetProvider> providers)
    {
        if (providers.Count == 0)
            throw new ArgumentException("A composite asset provider needs at least one provider.", nameof(providers));

        _providers = providers;
    }

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
