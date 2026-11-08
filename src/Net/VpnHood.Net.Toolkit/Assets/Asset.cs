namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// Represents the asset a provider serves at a path: the two as one value, to hand about. It answers what the provider
/// answers.
/// </summary>
/// <param name="assetProvider">The provider.</param>
/// <param name="assetPath">The asset's path.</param>
public class Asset(IAssetProvider assetProvider, string assetPath) : IAsset
{
    /// <inheritdoc/>
    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        return assetProvider.OpenReadAsync(assetPath, cancellationToken);
    }

    /// <summary>Returns the asset's path.</summary>
    /// <returns>The path.</returns>
    public override string ToString()
    {
        return assetPath;
    }
}
