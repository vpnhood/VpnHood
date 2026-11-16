using System.Reflection;

namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// Provides the assets an assembly carries as embedded resources, each named by the prefix followed by the asset path.
/// They are in memory, so their streams can seek.
/// </summary>
/// <param name="assembly">The assembly.</param>
/// <param name="resourcePrefix">The prefix of the resources' names.</param>
public class EmbeddedResourceAssetProvider(Assembly assembly, string resourcePrefix) : IAssetProvider
{
    /// <inheritdoc/>
    public Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
    {
        var stream = assembly.GetManifestResourceStream(resourcePrefix + assetPath)
                     ?? throw new AssetNotFoundException(assetPath);
        return Task.FromResult(stream);
    }
}
