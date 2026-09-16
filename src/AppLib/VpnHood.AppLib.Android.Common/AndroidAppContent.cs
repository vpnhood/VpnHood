using Android.Content;
using Android.Content.Res;
using VpnHood.AppLib.Assets;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Droid.Common;

// The assets folder on Android. The package carries it once, in base/assets, which the platform
// does not split by CPU architecture - but a package's assets are streams, not files, and the UI
// and the web server read files. So the folder is copied out to storage, once per version of it
// (assets/version.txt names the version, written by _sync-assets.ps1), and read from there;
// VpnHoodAndroidApp.Init names the copy to AppContent, which makes it the first time the folder
// is asked for. An older version's copy goes on the way.
public static class AndroidAppContent
{
    private const string VersionFileName = "version.txt";

    public static string Extract(Context context, string storageFolderPath)
    {
        var assets = context.Assets ?? throw new InvalidOperationException("The Android context has no asset manager.");
        var version = ReadVersion(assets);
        var rootPath = Path.Combine(storageFolderPath, AppContent.FolderName);
        var folderPath = Path.Combine(rootPath, version);
        if (File.Exists(Path.Combine(folderPath, VersionFileName)))
            return folderPath;

        // an older version's copy, and a copy this version never finished
        if (Directory.Exists(rootPath))
            VhUtils.TryInvoke("Delete the old assets folder", () => Directory.Delete(rootPath, true));

        // copied whole before it is named: the version file lands last, with the move, so a copy
        // cut short is never taken for a complete one
        var tempPath = folderPath + ".tmp";
        CopyFolder(assets, AppContent.FolderName, tempPath);
        Directory.Move(tempPath, folderPath);
        return folderPath;
    }

    private static string ReadVersion(AssetManager assets)
    {
        using var stream = assets.Open($"{AppContent.FolderName}/{VersionFileName}");
        using var reader = new StreamReader(stream);
        var version = reader.ReadToEnd().Trim();
        return version.Length > 0
            ? version
            : throw new InvalidOperationException("The package's assets folder names no version.");
    }

    private static void CopyFolder(AssetManager assets, string assetPath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        foreach (var name in assets.List(assetPath) ?? []) {
            var childPath = $"{assetPath}/{name}";
            // a folder lists its entries; a file lists none
            if (assets.List(childPath) is { Length: > 0 }) {
                CopyFolder(assets, childPath, Path.Combine(targetPath, name));
                continue;
            }

            using var source = assets.Open(childPath);
            using var target = File.Create(Path.Combine(targetPath, name));
            source.CopyTo(target);
        }
    }
}
