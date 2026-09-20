using Avalonia.Media;
using Avalonia.Media.Fonts;
using VpnHood.AppUi.Services;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;

// The fonts of the asset store, as one Avalonia font collection: the text faces (Poppins, and the
// Persian and Arabic faces the web UI lists beside it) and the icon font, out of the store rather
// than out of this assembly, so the bytes ship once - and as bytes the provider read, since the
// store may be a web server.
//
// A collection rather than a path in the XAML: Avalonia resolves a font family through its asset
// loader, which knows avares:// and nothing of a store chosen at run time - but a collection is
// named by a URI of its own, so "fonts:VpnHood#Poppins" in a style resolves here instead.
// FontCollectionBase does the matching, including the character fallback that puts a Persian word
// on a Persian face when Poppins has no glyph for it.
internal sealed class AppFontCollection : FontCollectionBase
{
    public const string Scheme = AppFonts.CollectionScheme;

    private AppFontCollection(IReadOnlyList<byte[]> fontFiles)
    {
        foreach (var fontFile in fontFiles) {
            using var stream = new MemoryStream(fontFile);
            if (!TryAddGlyphTypeface(stream, out _))
                throw new InvalidOperationException("A font of the asset store could not be read.");
        }
    }

    public override Uri Key { get; } = new(Scheme, UriKind.Absolute);

    // Every face the store lists, in one collection: a family is matched against all of them, and a
    // character the named family has no glyph for is matched against the rest - which is what puts
    // a Persian or Arabic word on its own face while the rest of the UI stays on Poppins.
    public static void Register(IReadOnlyList<byte[]> fontFiles)
    {
        if (fontFiles.Count == 0)
            throw new InvalidOperationException("The asset store lists no fonts (fonts/index.json).");

        FontManager.Current.AddFontCollection(new AppFontCollection(fontFiles));
    }
}
