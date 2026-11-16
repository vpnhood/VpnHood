namespace VpnHood.AppUi.Common;

// The names a UI gives the fonts of the asset store (fonts/, the faces listed in fonts/index.json):
// the text face, Poppins, with the Persian and Arabic faces beside it, and the icon font, Material
// Design Icons, cut down to the icons the UIs name. One collection under one scheme, so
// "fonts:VpnHood#Poppins" in a style resolves to the store wherever the store is; the collection
// itself is the UI toolkit's to register (AppFontCollection in the Avalonia UI).
//
// A browser page has a second folder beside it, browser-fonts/, for what an app gets from its OS: no
// browser lends a page a system font, so Russian and Chinese need faces of their own there.

/// <summary>
/// Provides the names a UI gives the fonts of the UI's content store: the text face, Poppins, with the Persian and
/// Arabic faces beside it, and the icon font, under one collection scheme.
/// </summary>
public static class AppFonts
{
    /// <summary>The store's folder of fonts.</summary>
    public const string FolderName = "fonts";

    /// <summary>The index of the fonts folder's faces.</summary>
    public const string IndexPath = $"{FolderName}/index.json";

    /// <summary>The store's folder of the faces a browser page needs for what an app gets from its system, as Russian and Chinese.</summary>
    public const string BrowserFolderName = "browser-fonts";

    /// <summary>The index of the browser fonts folder's faces.</summary>
    public const string BrowserIndexPath = $"{BrowserFolderName}/index.json";

    /// <summary>The scheme of the font collection, under which a style names a face.</summary>
    public const string CollectionScheme = "fonts:VpnHood";

    /// <summary>The text face's family, Poppins.</summary>
    public const string TextFamily = $"{CollectionScheme}#Poppins";
    internal const string IconFamily = $"{CollectionScheme}#Material Design Icons";
}
