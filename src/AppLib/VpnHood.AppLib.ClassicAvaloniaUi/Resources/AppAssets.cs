using Avalonia.Media;
using Avalonia.Media.Imaging;
using VpnHood.AppLib.Assets;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Resources;

// The assets folder as Avalonia draws it: the images and the country flags as bitmaps, decoded
// once, and the fonts as the collection the styles name. Where the folder is and what is in it is
// AppContent's business (VpnHood.AppLib.Assets); this class only turns its files into what a view
// can show.
public static class AppAssets
{
    private static bool _fontsRegistered;

    // The fonts, which every page's text needs and which Avalonia reaches through a collection
    // rather than a path (see AppFontCollection). Once, before the first text is drawn: as the
    // application initializes where the folder is in place by then (a desktop, iOS, a browser), and
    // in the activity on Android, where the Application starts Avalonia before the folder is named.
    public static void RegisterFonts()
    {
        if (_fontsRegistered)
            return;

        AppFontCollection.Register(AppContent.PathOf(AppFonts.FolderName));
        _fontsRegistered = true;
    }

    // An image of the folder, by its name there (images/rocket.webp is "rocket.webp"). Decoded
    // once: the pages ask for the same handful over and over, and a bitmap is immutable.
    public static Bitmap Image(string name)
    {
        return Cached(Images, $"images/{name}") ??
               throw new FileNotFoundException($"The assets folder has no image '{name}'.", AppContent.PathOf($"images/{name}"));
    }

    // The flag of a country, one PNG per ISO code. Null for the automatic location and for a code
    // with no flag, so a binding shows nothing rather than a broken image.
    public static Bitmap? Flag(string? countryCode)
    {
        if (string.IsNullOrEmpty(countryCode) || countryCode == "*")
            return null;

        return Cached(Flags, $"flags/{countryCode.ToLowerInvariant()}.png");
    }

    private static readonly Dictionary<string, Bitmap?> Images = [];
    private static readonly Dictionary<string, Bitmap?> Flags = [];

    private static Bitmap? Cached(Dictionary<string, Bitmap?> cache, string relativePath)
    {
        lock (cache) {
            if (cache.TryGetValue(relativePath, out var cached))
                return cached;
        }

        var path = AppContent.PathOf(relativePath);
        var bitmap = File.Exists(path) ? new Bitmap(path) : null;
        lock (cache) {
            cache[relativePath] = bitmap;
            return bitmap;
        }
    }

    // The text font as a family, for the one place XAML does not reach. It falls through to the
    // Persian and Arabic faces by itself - they are in the same collection, and a character Poppins
    // does not have is matched against the rest of it.
    public static FontFamily TextFont => FontFamily.Parse(AppFonts.TextFamily);
}
