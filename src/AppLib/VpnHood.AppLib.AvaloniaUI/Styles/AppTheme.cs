using Avalonia.Markup.Xaml.Styling;

namespace VpnHood.AppLib.AvaloniaUI.Styles;

// The product's theme. The web UI keeps one per product - the same keys, two sets of values
// (theme/themes.ts) - and picks it at startup from AppFeatures.UiName, the client's when a head
// names none (main.ts); these are those two. The app merges the chosen one before it loads the
// styles that read it, because a key is looked up once, where it is written.
public static class AppTheme
{
    public static ResourceInclude FromUiName(string? uiName)
    {
        var fileName = AppProduct.IsConnect(uiName) ? "ConnectTheme" : "ClientTheme";
        return new ResourceInclude(baseUri: null) {
            Source = new Uri($"avares://VpnHood.AppLib.AvaloniaUI/Styles/{fileName}.axaml")
        };
    }
}
