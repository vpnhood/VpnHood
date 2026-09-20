using Avalonia.Markup.Xaml.Styling;
using VpnHood.AppUi.Hosting.Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Styles;

// The look. The web UI keeps one theme per look - the same keys, two sets of values
// (theme/themes.ts) - and picks it at startup from AppFeatures.UiTheme (main.ts); these are those
// two. Blue is the default, merged by App.axaml itself so the tooling can follow every key to its
// definition; violet is merged over it in code once the XAML is loaded, and a dictionary merged
// later wins.
public static class AppTheme
{
    // the theme to merge over the default for this look; null when the default is its own
    public static ResourceInclude? OverrideFor(string? uiTheme)
    {
        if (uiTheme != "violet")
            return null;

        return new ResourceInclude(baseUri: null) {
            Source = new Uri("avares://VpnHood.AppUi.Presentation.Classic.Avalonia/Styles/VioletTheme.axaml")
        };
    }
}
