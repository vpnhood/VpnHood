using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.App.StoreScreenshots;

// Where the UI's pictures, faces and words are read from for a run: the folder _sync-assets.ps1
// mirrors them into, or the one zip a shipped head carries them in. Named on the command line
// rather than placed by the assets targets, because the caller says which store to draw with -
// a fork's own look is a different folder, not a different build of this tool.
internal static class Store
{
    public static IAssetProvider Open(string path)
    {
        if (Directory.Exists(path))
            return new FolderAssetProvider(path);
        if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return new ZipAssetProvider(new FileAsset(path), Path.Combine(Path.GetTempPath(), "VpnHoodStoreScreenshots", "store"));
        throw new FileNotFoundException($"--assets must name the store folder or its ui.zip: {path}");
    }
}
