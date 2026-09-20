namespace VpnHood.AppUi.Services;

// The names a UI gives the fonts of the asset store (fonts/, the faces listed in fonts/index.json):
// the text face, Poppins, with the Persian and Arabic faces beside it, and the icon font, Material
// Design Icons, cut down to the icons the UIs name. One collection under one scheme, so
// "fonts:VpnHood#Poppins" in a style resolves to the store wherever the store is; the collection
// itself is the UI toolkit's to register (AppFontCollection in the Avalonia UI).
public static class AppFonts
{
    public const string FolderName = "fonts";
    public const string IndexPath = $"{FolderName}/index.json";
    public const string CollectionScheme = "fonts:VpnHood";
    public const string TextFamily = $"{CollectionScheme}#Poppins";
    public const string IconFamily = $"{CollectionScheme}#Material Design Icons";
}
