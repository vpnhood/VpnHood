namespace VpnHood.AppLib.Assets;

// The names a UI gives the fonts of the assets folder (assets/fonts): the text face, Poppins, with
// the Persian and Arabic faces beside it, and the icon font, Material Design Icons, cut down to
// the icons the UIs name. One collection under one scheme, so "fonts:VpnHood#Poppins" in a style
// resolves to the folder wherever the folder is; the collection itself is the UI toolkit's to
// register (AppFontCollection in the Avalonia UI).
public static class AppFonts
{
    public const string FolderName = "fonts";
    public const string CollectionScheme = "fonts:VpnHood";
    public const string TextFamily = $"{CollectionScheme}#Poppins";
    public const string IconFamily = $"{CollectionScheme}#Material Design Icons";
}
