using Avalonia.Markup.Xaml.Styling;

namespace VpnHood.AppLib.AvaloniaUI.Styles;

// The product's theme. The web UI keeps one per product - the same keys, two sets of values
// (theme/themes.ts) - and picks it at startup from AppFeatures.UiName, the client's when a head
// names none (main.ts); these are those two. The app merges the chosen one before it loads the
// styles that read it, because a key is looked up once, where it is written.
public static class AppTheme
{
    // what a Connect head sets as its AppOptions.UiName; a Client head sets nothing
    public const string ConnectUiName = "VpnHoodConnect";

    public static ResourceInclude FromUiName(string? uiName)
    {
        var fileName = uiName == ConnectUiName ? "ConnectTheme" : "ClientTheme";
        return new ResourceInclude(baseUri: null) {
            Source = new Uri($"avares://VpnHood.AppLib.AvaloniaUI/Styles/{fileName}.axaml")
        };
    }
}
