using VpnHood.AppLib.App;
using VpnHood.AppUi.Common;

namespace VpnHood.AppUi.Hosting.WebView;

// The app's theme colours (ThemeColors), where the web UI runs in its process (Android, iOS, MAUI):
// read once, for every view that draws with them.
public static class WebViewTheme
{
    private static readonly Lazy<Task<ThemeColors>> InProcessColors = new(() =>
        ThemeColors.LoadAsync(VpnHoodApp.Instance.UiAssetProvider, VpnHoodApp.Instance.Features.UiTheme,
            CancellationToken.None));

    public static Task<ThemeColors> Colors => InProcessColors.Value;
}
