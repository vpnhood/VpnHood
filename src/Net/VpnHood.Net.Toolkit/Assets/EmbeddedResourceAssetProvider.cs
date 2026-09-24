using System.Reflection;

namespace VpnHood.Net.Toolkit.Assets;

// Assets an assembly carries as embedded resources: the prefix and the asset path make the resource
// name. Already in memory, so these can seek.
public class EmbeddedResourceAssetProvider(Assembly assembly, string resourcePrefix) : IAssetProvider
{
    public Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
    {
        var stream = assembly.GetManifestResourceStream(resourcePrefix + assetPath)
                     ?? throw new AssetNotFoundException(assetPath);
        return Task.FromResult(stream);
    }
}
