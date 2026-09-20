using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VpnHood.AppUi.Common;
using VpnHood.Core.Toolkit.Assets;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Streams;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;

// The asset store as Avalonia draws it: the fonts as the collection the styles name, the images
// and the country flags as bitmaps, decoded once each. Where the files come from is the provider
// the head hands in (PrepareAsync) - a folder in process, the app's web host from a browser page -
// and this class only turns them into what a view can show. Nothing here waits: a bitmap is asked
// for asynchronously (AppImage), and the fonts are fetched before the first view.
public static class AppAssets
{
    private static IAssetProvider? _assets;
    private static IReadOnlyList<byte[]>? _fontFiles;
    private static bool _fontsRegistered;
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Bitmaps = new();

    private static IAssetProvider Assets => _assets ?? throw new InvalidOperationException(
        $"The assets have not been prepared: {nameof(AppAssets)}.{nameof(PrepareAsync)} runs before the first view.");

    public static bool IsPrepared => _fontFiles != null;

    // The provider, and the fonts out of it - every face fonts/index.json names - which every
    // page's text needs before it is drawn. Registered at once where Avalonia is already up
    // (Android, whose Application starts before its activity prepares the content); otherwise
    // Initialize registers them as the styles that name them are read.
    public static async Task PrepareAsync(IAssetProvider assets, CancellationToken cancellationToken)
    {
        _assets = assets;
        var names = await AssetIndex.ReadAsync(assets, AppFonts.IndexPath, cancellationToken).Vhc();
        var files = new List<byte[]>();
        foreach (var name in names) {
            await using var stream = await assets.OpenReadAsync($"{AppFonts.FolderName}/{name}", cancellationToken).Vhc();
            var memoryStream = await stream.ToMemoryStreamAsync(cancellationToken).Vhc();
            files.Add(memoryStream.ToArray());
        }

        _fontFiles = files;
        if (Application.Current != null)
            RegisterFonts();
    }

    public static void RegisterFonts()
    {
        if (_fontsRegistered)
            return;

        AppFontCollection.Register(_fontFiles ?? throw new InvalidOperationException(
            $"The fonts have not been read: {nameof(AppAssets)}.{nameof(PrepareAsync)} runs before the first view."));
        _fontsRegistered = true;
    }

    // An image of the store by its path ("images/rocket.webp", "flags/us.png"), decoded once: the
    // pages ask for the same handful over and over, and a bitmap is immutable. Null for a path
    // there is no file for, so a binding shows nothing rather than a broken image.
    public static Task<Bitmap?> LoadBitmapAsync(string assetPath)
    {
        return Bitmaps.GetOrAdd(assetPath, LoadBitmap);
    }

    private static async Task<Bitmap?> LoadBitmap(string assetPath)
    {
        var stream = await Assets.TryOpenReadAsync(assetPath, CancellationToken.None).Vhc();
        if (stream == null)
            return null;

        // the decoder reads a picture's header and then its body, so it must be able to rewind: a
        // file does it where it lies, and only one fetched over HTTP is bought into memory
        await using var seekable = await stream.ToMemoryStreamIfNotSeekableAsync(CancellationToken.None).Vhc();
        return new Bitmap(seekable);
    }

    // A text file of the store (a content document); null when it is not there, which is how it is
    // asked for: a document this build did not ship in a language falls back to English.
    public static async Task<string?> ReadTextAsync(string assetPath, CancellationToken cancellationToken)
    {
        await using var stream = await Assets.TryOpenReadAsync(assetPath, cancellationToken).Vhc();
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).Vhc();
    }

    // An image of the store, by its name there (images/rocket.webp is "rocket.webp").
    public static string ImagePath(string name)
    {
        return $"images/{name}";
    }

    // The flag of a country, one PNG per ISO code. Null for the automatic location and for no code,
    // so a binding shows nothing.
    public static string? FlagPath(string? countryCode)
    {
        return string.IsNullOrEmpty(countryCode) || countryCode == "*" ? null : $"flags/{countryCode.ToLowerInvariant()}.png";
    }

    // The text font as a family, for the one place XAML does not reach. It falls through to the
    // Persian and Arabic faces by itself - they are in the same collection, and a character Poppins
    // does not have is matched against the rest of it.
    public static FontFamily TextFont => FontFamily.Parse(AppFonts.TextFamily);
}
