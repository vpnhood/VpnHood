namespace VpnHood.Core.Toolkit.Assets;

// An asset a provider serves at a path: the two as one value, to hand about. It answers what the
// provider answers - the wrapper decides nothing.
public class Asset(IAssetProvider assetProvider, string assetPath) : IAsset
{
    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        return assetProvider.OpenReadAsync(assetPath, cancellationToken);
    }

    public override string ToString()
    {
        return assetPath;
    }
}
