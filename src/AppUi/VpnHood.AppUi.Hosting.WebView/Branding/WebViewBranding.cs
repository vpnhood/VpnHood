using VpnHood.AppLib.App;
using VpnHood.AppUi.Common;

namespace VpnHood.AppUi.Hosting.WebView.Branding;

// The app's branding (AppBranding), where the web UI runs in its process (Android, iOS, MAUI): read
// once, for every view that draws with it.
public static class WebViewBranding
{
    private static readonly Lazy<Task<AppBrandingColors>> InProcessColors = new(() =>
        AppBranding.LoadAsync(VpnHoodApp.Instance.UiAssetProvider, VpnHoodApp.Instance.Features.UiTheme));

    public static Task<AppBrandingColors> AppColors => InProcessColors.Value;
}
