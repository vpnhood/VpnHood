using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// Provides opening for a caller that only asks: <see langword="null"/> instead of <see cref="AssetNotFoundException"/>.
/// </summary>
/// <remarks>
/// Asking is normal: a language falls back to another, a picture that is not there shows nothing, a server answers 404.
/// Only what the provider classes as absent becomes <see langword="null"/>; a file it finds and cannot open still fails.
/// A file provider classes as absent a file <see cref="File.Exists"/> cannot see.
/// </remarks>
public static class AssetProviderExtensions
{
    /// <summary>Provides opening by path that gives <see langword="null"/> for an absent asset.</summary>
    /// <param name="assetProvider">The provider.</param>
    extension(IAssetProvider assetProvider)
    {
        /// <summary>Opens the asset at a path, or gives <see langword="null"/> when there is none.</summary>
        /// <param name="assetPath">The asset's path.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A task whose result is a new stream, which the caller owns and disposes, or <see langword="null"/>.
        /// </returns>
        public async Task<Stream?> TryOpenReadAsync(string assetPath, CancellationToken cancellationToken)
        {
            try {
                return await assetProvider.OpenReadAsync(assetPath, cancellationToken).Vhc();
            }
            catch (AssetNotFoundException) {
                return null;
            }
        }
    }

    /// <summary>Provides opening that gives <see langword="null"/> for an absent asset.</summary>
    /// <param name="asset">The asset.</param>
    extension(IAsset asset)
    {
        /// <summary>Opens the asset, or gives <see langword="null"/> when there is none.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A task whose result is a new stream, which the caller owns and disposes, or <see langword="null"/>.
        /// </returns>
        public async Task<Stream?> TryOpenReadAsync(CancellationToken cancellationToken)
        {
            try {
                return await asset.OpenReadAsync(cancellationToken).Vhc();
            }
            catch (AssetNotFoundException) {
                return null;
            }
        }
    }
}
