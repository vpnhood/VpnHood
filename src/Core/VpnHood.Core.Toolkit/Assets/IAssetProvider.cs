namespace VpnHood.Core.Toolkit.Assets;

// How a file an asset package's build placed is read on this platform.
//
// An asset package ships bytes and MSBuild XML and no code, so its build drops its files wherever
// the platform keeps them - beside the executable on Windows and Linux, in the bundle on Apple,
// inside the package itself on Android, where they are not files at all. This is the single seam
// across that difference: one implementation per PLATFORM, never one per package, so a new asset
// package is a new path and nothing else.
public interface IAssetProvider
{
    // The file at an asset path - "iplocations/IpLocations.zip" - as a seekable, read-only stream
    // the caller owns and disposes. Seekable because that is what reading an archive's entry needs,
    // and because a platform that cannot seek would have to copy the whole file to pretend it can.
    Stream OpenRead(string assetPath);
}

