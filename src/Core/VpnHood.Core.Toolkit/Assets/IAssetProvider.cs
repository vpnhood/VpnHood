namespace VpnHood.Core.Toolkit.Assets;

// Where a build's data files ended up on this platform, read by name. One implementation per
// PLATFORM rather than per set of files - beside the executable, inside an app bundle, inside the
// package on Android, or over HTTP for a page that has nothing placed beside it - so a new set of
// files is a new path and nothing else.
public interface IAssetProvider
{
    // The asset at a path ("fonts/text.ttf"), as a readable stream the caller owns and disposes; a
    // new stream every call. Asynchronous because a page in a browser fetches over the network and
    // cannot wait.
    //
    // No such asset throws AssetNotFoundException, and that type exactly: a caller that is asking
    // rather than requiring - a language falling back to another, a picture that may not exist -
    // takes TryOpenReadAsync, which catches it and hands back null.
    //
    // Read forward, and nothing more. Whether it can seek is the platform's answer, not a promise:
    // a file can, an HTTP body and an Android asset cannot. A caller that needs more than one pass
    // asks for the asset twice where it can, and buys it into memory only where it cannot
    // (StreamExtensions.ToMemoryStreamIfNotSeekableAsync) - so the cost is paid where it is decided.
    Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken);
}
