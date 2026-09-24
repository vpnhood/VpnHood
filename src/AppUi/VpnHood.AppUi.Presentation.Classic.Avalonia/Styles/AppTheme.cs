using Avalonia.Controls;
using VpnHood.AppUi.Hosting.Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Styles;

// The look. The web UI keeps one theme per look - the same keys, two sets of values
// (theme/themes.ts) - and picks it at startup from AppFeatures.UiTheme (main.ts); these are those
// two. Blue is the default and violet is merged over it, both by Initialize, before the App loads
// the XAML whose styles read the keys - a dictionary merged later wins, a dictionary merged after
// the styles wins nothing.
public static class AppTheme
{
    // the theme to merge over the default for this look; null when the default is its own. The
    // dictionary is constructed, never named by Uri: a Uri is a string to the trimmer, which drops
    // what no code reaches - and the browser bundle is trimmed to the full extent.
    public static ResourceDictionary? OverrideFor(string? uiTheme)
    {
        return uiTheme == "violet" ? new VioletTheme() : null;
    }
}
