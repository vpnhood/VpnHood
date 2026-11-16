namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// Represents one thing to open: a provider with its path already chosen. It names the asset, so a failure can name it,
/// where a bare delegate could only say that something failed.
/// </summary>
/// <remarks>
/// Take this in a signature, and make an <see cref="Asset"/>, or a <see cref="FileAsset"/> for a path: where the bytes
/// come from is the caller's business. It keeps the contract of <see cref="IAssetProvider.OpenReadAsync"/>: a new
/// stream every call, which the caller owns and disposes, read forward, and <see cref="AssetNotFoundException"/> when
/// there is none. A caller that only asks takes <c>TryOpenReadAsync</c>.
/// </remarks>
public interface IAsset
{
    /// <summary>Opens the asset.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is a new stream, which the caller owns and disposes; read it forward.</returns>
    /// <exception cref="AssetNotFoundException">There is no such asset.</exception>
    Task<Stream> OpenReadAsync(CancellationToken cancellationToken);
}
