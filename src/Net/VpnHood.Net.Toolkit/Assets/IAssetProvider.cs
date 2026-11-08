namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// Represents where a build's data files ended up on this platform, read by path: beside the executable, inside an app
/// bundle, inside an Android package, or over HTTP for a page that has nothing placed beside it.
/// </summary>
/// <remarks>
/// There is one implementation per platform, not per set of files, so a new set of files is a new path and nothing else.
/// </remarks>
public interface IAssetProvider
{
    /// <summary>Opens the asset at a path, such as <c>fonts/text.ttf</c>.</summary>
    /// <remarks>
    /// It is asynchronous because a page in a browser fetches over the network and cannot wait. Read the stream
    /// forward: whether it can seek is the platform's answer, not a promise; a file can, an HTTP body and an Android
    /// asset cannot. A caller that needs more than one pass opens the asset again where it can, and copies it into
    /// memory only where it cannot, so the cost is paid where it is decided. A caller that only asks, such as a language
    /// falling back to another, takes <c>TryOpenReadAsync</c>, which gives <see langword="null"/> for an absent asset.
    /// </remarks>
    /// <param name="assetPath">The asset's path.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is a new stream, which the caller owns and disposes.</returns>
    /// <exception cref="AssetNotFoundException">
    /// The provider classes the asset as absent; exactly this type, as a file it finds and cannot open is another failure.
    /// </exception>
    Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken);
}
