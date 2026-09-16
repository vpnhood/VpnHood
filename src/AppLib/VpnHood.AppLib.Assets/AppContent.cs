using System.Diagnostics.CodeAnalysis;

namespace VpnHood.AppLib.Assets;

// The folder the app's files are read from - the images, the country flags, the fonts, the content
// documents - as files under their own names, so a UI reads them as it does anywhere, by path, and
// the web server serves them by the same names. Where the folder is depends on how the platform
// packages files: beside the executable on a desktop and in the bundle on iOS, where the build put
// it (build/VpnHood.AppLib.Assets.targets), so nothing names it; on Android, where a package's
// assets are not files, a copy in storage that the platform code makes and names through
// FolderResolver, once, before the folder is first asked for. The words are not here: Strings
// carries them in this assembly.
public static class AppContent
{
    public const string FolderName = "assets";
    private static string? _folderPath;

    // Set by a platform package when the folder is not the one beside the app: Android names the
    // copy it makes; the browser page names the folder it fetched into. Read the first time the
    // folder is asked for, after which the answer stands.
    public static Func<string>? FolderResolver { get; set; }

    public static bool IsResolved => _folderPath != null;

    public static string FolderPath => _folderPath ??= Resolve();

    // The folder when it can be found, for a server that answers a request rather than draws a
    // page: a build with no folder (a test's) gets no file, not a fault.
    public static bool TryGetFolderPath([NotNullWhen(true)] out string? folderPath)
    {
        if (_folderPath == null) {
            var path = FolderResolver?.Invoke() ?? DefaultFolderPath;
            if (!Directory.Exists(path)) {
                folderPath = null;
                return false;
            }

            _folderPath = path;
        }

        folderPath = _folderPath;
        return true;
    }

    private static string DefaultFolderPath => Path.Combine(AppContext.BaseDirectory, FolderName);

    private static string Resolve()
    {
        var path = FolderResolver?.Invoke() ?? DefaultFolderPath;
        return Directory.Exists(path)
            ? path
            : throw new DirectoryNotFoundException(
                $"The assets folder does not exist. The app's build places it (VpnHood.AppLib.Assets.targets); " +
                $"a platform that copies it names the copy through {nameof(AppContent)}.{nameof(FolderResolver)}. {path}");
    }

    public static string PathOf(string relativePath)
    {
        return Path.Combine(FolderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static bool Exists(string relativePath)
    {
        return File.Exists(PathOf(relativePath));
    }

    // A text file of the folder (a content document); null when it is not there, which is how it
    // is asked for: a document this build did not ship in a language falls back to English.
    public static string? ReadText(string relativePath)
    {
        var path = PathOf(relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static IReadOnlyList<string> FileNames(string relativeFolder, string pattern)
    {
        var folder = PathOf(relativeFolder);
        return Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, pattern).Select(Path.GetFileNameWithoutExtension).OfType<string>()]
            : [];
    }
}
