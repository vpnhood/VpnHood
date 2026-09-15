using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace VpnHood.AppLib.AvaloniaUI.Resources;

// The folder this UI draws from: the images, the country flags, the fonts, the locale files and
// the content documents, as files under their own names. It is the assets folder of the SPA
// bundle - the web UI loads the very same files from it over its web server - so a build that
// ships both UIs ships one copy of each. The head names the folder (Initialize) before the UI
// starts; it is a path and nothing more, so it can be any folder a head can fill.
public static class AppAssets
{
    private static string? _folderPath;

    // Set by the head before the UI starts. Today every head has the SPA bundle and hands over its
    // assets folder (VpnHoodAppWebServer.AssetsFolderPath); a head that fills a folder of its own
    // hands over that one instead - this is a path and nothing else.
    public static string FolderPath {
        get => _folderPath ?? throw new InvalidOperationException(
            $"The assets folder has not been named. A head must set {nameof(AppAssets)}.{nameof(FolderPath)} " +
            "before the UI starts - the assets folder of the SPA bundle, which the web server extracts.");
        set {
            if (!Directory.Exists(value))
                throw new DirectoryNotFoundException($"The assets folder does not exist. {value}");

            _folderPath = value;
        }
    }

    public static bool IsFolderPathSet => _folderPath != null;

    // The fonts, which every page's text needs and which Avalonia reaches through a collection
    // rather than a path (see AppFontCollection). Once, after the folder is named and before the
    // first text is drawn - which is as the app starts where a head names the folder first (a
    // desktop, iOS), and in the activity on Android, where the Application starts Avalonia before
    // any activity has named it.
    private static bool _fontsRegistered;

    public static void RegisterFonts()
    {
        if (_fontsRegistered)
            return;

        AppFontCollection.Register(PathOf("fonts"));
        _fontsRegistered = true;
    }

    public static string PathOf(string relativePath)
    {
        return Path.Combine(FolderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    // An image of the folder, by its name there (images/rocket.webp is "rocket.webp"). Decoded
    // once: the pages ask for the same handful over and over, and a bitmap is immutable.
    public static Bitmap Image(string name)
    {
        return Cached(Images, $"images/{name}") ??
               throw new FileNotFoundException($"The assets folder has no image '{name}'.", PathOf($"images/{name}"));
    }

    // The flag of a country, one PNG per ISO code. Null for the automatic location and for a code
    // with no flag, so a binding shows nothing rather than a broken image.
    public static Bitmap? Flag(string? countryCode)
    {
        if (string.IsNullOrEmpty(countryCode) || countryCode == "*")
            return null;

        return Cached(Flags, $"flags/{countryCode.ToLowerInvariant()}.png");
    }

    // A text file of the folder (a locale, a content document); null when it is not there, which
    // is how both are asked for: a language this build did not ship falls back to English.
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

    private static readonly Dictionary<string, Bitmap?> Images = [];
    private static readonly Dictionary<string, Bitmap?> Flags = [];

    private static Bitmap? Cached(Dictionary<string, Bitmap?> cache, string relativePath)
    {
        lock (cache) {
            if (cache.TryGetValue(relativePath, out var cached))
                return cached;
        }

        var path = PathOf(relativePath);
        var bitmap = File.Exists(path) ? new Bitmap(path) : null;
        lock (cache) {
            cache[relativePath] = bitmap;
            return bitmap;
        }
    }

    // What the XAML names: the collection's key and the two families in it. The text font falls
    // through to the Persian and Arabic faces by itself - they are in the same collection, and a
    // character Poppins does not have is matched against the rest of it.
    public const string TextFontFamily = $"{AppFontCollection.Scheme}#Poppins";
    public const string IconFontFamily = $"{AppFontCollection.Scheme}#Material Design Icons";

    public static FontFamily TextFont => FontFamily.Parse(TextFontFamily);
}
