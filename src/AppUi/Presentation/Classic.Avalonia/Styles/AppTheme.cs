using Avalonia.Markup.Xaml.Styling;
using VpnHood.AppUi.Hosting.Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Styles;

// The product's theme. The web UI keeps one per product - the same keys, two sets of values
// (theme/themes.ts) - and picks it at startup from AppFeatures.UiName, the client's when a head
// names none (main.ts); these are those two. The client's is the default, merged by App.axaml
// itself so the tooling can follow every key to its definition; the other product's is merged
// over it in code once the XAML is loaded, and a dictionary merged later wins.
public static class AppTheme
{
    // the theme to merge over the default for this product; null when the default is its own
    public static ResourceInclude? OverrideFor(string? uiName)
    {
        if (!AppProduct.IsConnect(uiName))
            return null;

        return new ResourceInclude(baseUri: null) {
            Source = new Uri("avares://VpnHood.AppUi.Presentation.Classic.Avalonia/Styles/ConnectTheme.axaml")
        };
    }
}
