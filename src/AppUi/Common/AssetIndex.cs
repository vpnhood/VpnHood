using System.Text.Json;
using VpnHood.Core.Toolkit.Assets;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppUi.Common;

// The names of the files in a folder of the store, as the store declares them (index.json, written
// beside them by _sync-assets.ps1): the languages under locales/, the faces under fonts/. A list in
// the store rather than a listing of it, because a provider answers by name and never enumerates -
// a page in a browser could not.
public static class AssetIndex
{
    public static async Task<IReadOnlyList<string>> ReadAsync(IAssetProvider assets, string indexPath, CancellationToken cancellationToken)
    {
        await using var stream = await assets.OpenReadAsync(indexPath, cancellationToken).Vhc();
        return await JsonSerializer.DeserializeAsync(stream, AssetJsonContext.Default.StringArray, cancellationToken).Vhc() ?? [];
    }
}
