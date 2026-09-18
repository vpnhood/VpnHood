using VpnHood.AppLib.Assets;
using VpnHood.AppLib.WebHosting;

namespace VpnHood.AppLib.Api.WebHost;

// What the local and the remote host have in common, owned by the factory that makes them: the UI's
// files and the content package's file list. Both resolved on first read, so a head that opens neither
// host pays for neither. Ports are not here - each host resolves its own, so neither has to reach for
// the other to learn where it is.
internal class WebHostShared(WebHostOptions options, WebHostCreateParams createParams)
{
    private readonly Lock _lock = new();

    public WebRoot WebRoot {
        get {
            lock (_lock)
                return field ??= new WebRoot(options.WebRootZip, createParams.StorageFolderPath);
        }
    }

    // Every file under the content package's assets folder, by its name relative to it, the folder's
    // separators as URL segments. The list belongs to the package rather than to a host, and the
    // package cannot change while the app runs, so it is walked once for both.
    public IReadOnlyList<string> AssetNames {
        get {
            lock (_lock)
                return field ??= ReadAssetNames();
        }
    }

    private static IReadOnlyList<string> ReadAssetNames()
    {
        if (!AppContent.TryGetFolderPath(out var assetsPath))
            return [];

        return Directory.EnumerateFiles(assetsPath, "*", SearchOption.AllDirectories)
            .Select(x => Path.GetRelativePath(assetsPath, x).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
