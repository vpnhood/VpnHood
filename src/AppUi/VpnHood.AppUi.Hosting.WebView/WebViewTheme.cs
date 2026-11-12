using VpnHood.AppLib.App;
using VpnHood.AppUi.Common;

namespace VpnHood.AppUi.Hosting.WebView;

/// <summary>
/// Provides the app's theme colours where the web UI runs in the app's process, as on Android, iOS and MAUI: read once,
/// for every view that draws with them.
/// </summary>
public static class WebViewTheme
{
    private static readonly Lazy<Task<ThemeColors>> InProcessColors = new(() =>
        ThemeColors.LoadAsync(VpnHoodApp.Instance.UiAssetProvider, VpnHoodApp.Instance.Descriptor.UiTheme,
            CancellationToken.None));

    /// <summary>
    /// Gets the theme's colours, read once from the UI's content, or the defaults where it has none or cannot be read.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The property was first read before the app started; the failure is kept, so later reads throw too.
    /// </exception>
    public static Task<ThemeColors> Colors => InProcessColors.Value;
}
