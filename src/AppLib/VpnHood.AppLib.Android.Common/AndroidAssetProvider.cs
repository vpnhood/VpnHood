using Android.Content;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.AppLib.Droid.Common;

// Asset files on Android: entries of the .apk, under base/assets, which the platform does not split
// by CPU architecture - which is why data ships as placed files rather than inside an assembly, and
// is carried once instead of once per ABI.
//
// What AssetManager hands out is read forward and cannot seek, and that is what this hands on: a
// caller that needs to rewind buys it into memory (ToMemoryStreamIfNotSeekableAsync), and one
// that does not never pays for a buffer. Every call opens the asset again, so a reader that needs
// two passes can take two. Not OpenFd's byte range into the .apk, which reads in place with no copy
// but only while the entry is stored rather than deflated AND the offset belongs to the file we
// think it does - and the second cannot be shown to hold for a split or an asset-pack delivery. It
// would not fail if it were wrong; it would return whatever bytes lie at that offset.
public class AndroidAssetProvider(Context context) : IAssetProvider
{
    public Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
    {
        var assets = context.Assets
            ?? throw new InvalidOperationException("The Android context has no asset manager.");

        try {
            return Task.FromResult<Stream>(assets.Open(assetPath));
        }
        catch (Java.IO.FileNotFoundException ex) {
            throw new AssetNotFoundException(assetPath, ex);
        }
    }
}
