using System.Text.Json;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Common;

// The names of the files in a folder of the store, as the store declares them (index.json, written
// beside them by _sync-assets.ps1): the languages under locales/, the faces under fonts/. A list in
// the store rather than a listing of it, because a provider answers by name and never enumerates -
// a page in a browser could not.

/// <summary>
/// Provides the names of the files in a folder of the UI's content store, as the store declares them in an index.
/// </summary>
public static class AssetIndex
{
    /// <summary>Reads a folder's index.</summary>
    /// <param name="assets">The UI's content store.</param>
    /// <param name="indexPath">The index's path in the store.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the names the index lists.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="System.Text.Json.JsonException">The index cannot be read.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.Assets.AssetNotFoundException">The store has no such index.</exception>
    public static async Task<IReadOnlyList<string>> ReadAsync(IAssetProvider assets, string indexPath, CancellationToken cancellationToken)
    {
        await using var stream = await assets.OpenReadAsync(indexPath, cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, AssetJsonContext.Default.StringArray, cancellationToken).ConfigureAwait(false) ?? [];
    }
}
