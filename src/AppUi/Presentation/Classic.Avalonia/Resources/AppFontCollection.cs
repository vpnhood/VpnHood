using Avalonia.Media;
using Avalonia.Media.Fonts;
using VpnHood.AppLib.Assets;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;

// The fonts of the assets folder, as one Avalonia font collection: the text faces (Poppins, and
// the Persian and Arabic faces the web UI lists beside it) and the icon font, read from files
// rather than from this assembly, because the SPA bundle already carries them and a second copy
// in the package is a second copy on the device.
//
// A collection rather than a path in the XAML: Avalonia resolves a font family through its asset
// loader, which knows avares:// and nothing of a folder chosen at run time - but a collection is
// named by a URI of its own, so "fonts:VpnHood#Poppins" in a style resolves here instead.
// FontCollectionBase does the matching, including the character fallback that puts a Persian word
// on a Persian face when Poppins has no glyph for it.
internal sealed class AppFontCollection : FontCollectionBase
{
    public const string Scheme = AppFonts.CollectionScheme;

    private AppFontCollection(string folderPath)
    {
        foreach (var file in Directory.EnumerateFiles(folderPath, "*.ttf").OrderBy(x => x, StringComparer.Ordinal)) {
            using var stream = File.OpenRead(file);
            if (!TryAddGlyphTypeface(stream, out _))
                throw new InvalidOperationException($"The font could not be read. {file}");
        }
    }

    public override Uri Key { get; } = new(Scheme, UriKind.Absolute);

    // Every face in the folder, in one collection: a family is matched against all of them, and a
    // character the named family has no glyph for is matched against the rest - which is what puts
    // a Persian or Arabic word on its own face while the rest of the UI stays on Poppins.
    public static void Register(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"The assets folder has no fonts folder. {folderPath}");

        FontManager.Current.AddFontCollection(new AppFontCollection(folderPath));
    }
}
