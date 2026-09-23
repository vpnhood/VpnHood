using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.Core.Toolkit.Assets;

// For a caller that is asking rather than requiring: null instead of AssetNotFoundException. Asking
// is normal - a language falls back to another, a document never translated falls back, a picture
// that is not there shows nothing, a server answers 404 - and this keeps the catch out of every
// such call, and narrow: only an absent asset is turned into null, so a file that exists and will
// not open still fails.
//
// On both, under one name, because both mean the same thing: an asset by path, and an asset whose
// path is already chosen.
public static class AssetProviderExtensions
{
    extension(IAssetProvider assetProvider)
    {
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

    extension(IAsset asset)
    {
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
