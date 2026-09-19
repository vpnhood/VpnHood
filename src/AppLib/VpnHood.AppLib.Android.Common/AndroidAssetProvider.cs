using Android.Content;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.AppLib.Droid.Common;

// Asset files on Android. A package carries them in base/assets, which the platform does not split
// by CPU architecture - that is why an asset package places files instead of compiling them into an
// assembly, and it is the whole point of the exercise: the bytes are carried once, not once per ABI.
//
// Reading them is the plain way. A package's assets are entries of the .apk rather than files, and
// AssetManager hands out a forward-only stream, so an asset is copied into memory and read from
// there. The offset trick - OpenFd's byte range into the .apk, read in place with no copy - works
// only while the entry is stored rather than deflated AND the offset belongs to the file we think
// it does, and the second of those cannot be shown to hold for a split or an asset-pack delivery.
// It would not fail if it were wrong; it would return whatever bytes lie at that offset. Not worth
// it for a database read a handful of times in a session.
//
// The copy is per read and not held: the caller disposes what it gets, so the memory goes back
// rather than sitting there for the life of the app the way the old embedded byte[] did.
public class AndroidAssetProvider(Context context) : IAssetProvider
{
    public Stream OpenRead(string assetPath)
    {
        var assets = context.Assets
            ?? throw new InvalidOperationException("The Android context has no asset manager.");

        using var source = assets.Open(assetPath);
        var memoryStream = new MemoryStream();
        source.CopyTo(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }
}
