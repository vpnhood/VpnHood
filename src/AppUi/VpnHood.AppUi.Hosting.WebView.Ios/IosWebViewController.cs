using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Ios;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.AppUi.Hosting.WebView;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.AppLib.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.WebView.Ios;

/// <summary>
/// Represents the page's controller on iOS: the iOS chrome around the web UI, its background, status bar and safe area,
/// with the native lifecycle handed to the shared <see cref="WebViewHost"/>.
/// </summary>
/// <remarks>
/// The app must have started with its local web host, which needs the app's web content, as the page comes from it.
/// </remarks>
public class IosWebViewController : UIViewController
{
    private WebViewHost? _host;
    private NSObject? _foregroundObserver;

    /// <summary>
    /// Gets a value indicating whether the controller asks for every orientation, rather than portrait only, as the web UI
    /// is portrait first; the app's and the device's own limits still apply.
    /// </summary>
    /// <remarks>On an iPad that shares its screen with other apps, the system may not honour the mask.</remarks>
    public bool AllowRotation { get; init; }

    /// <inheritdoc/>
    /// <remarks>Portrait, or every orientation with <see cref="AllowRotation"/>.</remarks>
    public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations() =>
        AllowRotation ? UIInterfaceOrientationMask.All : UIInterfaceOrientationMask.Portrait;

    /// <inheritdoc/>
    /// <remarks>Light content, always, whatever the background.</remarks>
    public override UIStatusBarStyle PreferredStatusBarStyle() => UIStatusBarStyle.LightContent;

    /// <inheritdoc/>
    /// <remarks>
    /// It gives the app its UI context, then paints the view and makes the web view once the theme's colours are read.
    /// </remarks>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();

        // Publish the UI context so the core/web-server can perform UI-bound operations.
        AppUiContext.Context = new IosUiContext();
        _ = CreateContentWhenReady();
    }

    // The view is painted and the web view made once the look they draw with is final - nothing
    // shows before, where a colour that changed under the user would be seen. The wait comes back
    // to the main thread, where views are touched.
    private async Task CreateContentWhenReady()
    {
        try {
            // Edge-to-edge: the whole window, the status-bar and home-indicator safe areas included, in
            // the theme's window background, so the system bars blend into the app, as on Android.
            var backgroundColor = ToUiColor((await WebViewTheme.Colors).WindowBackgroundColor) ?? UIColor.SystemBackground;
            View!.BackgroundColor = backgroundColor;

            // the app's own web host: the platform started the app in this process
            var webView = new IosWebView(this, backgroundColor);
            var webHost = VpnHoodApp.Instance.LocalWebHost ??
                          throw new InvalidOperationException("This app was given no web host, so its SPA cannot be shown.");
            _host = new WebViewHost(webView, new AppWebViewPageHost(webHost));
            _host.Start();

            // iOS suspends the host app in the background and can close the loopback socket meanwhile;
            // the web server re-checks itself on this signal.
            _foregroundObserver = UIApplication.Notifications.ObserveWillEnterForeground(
                (_, _) => _host?.OnResume());
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create the app's web view.");
        }
    }

    private static UIColor? ToUiColor(VhColor? color)
    {
        return color is { } c
            ? UIColor.FromRGBA(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f)
            : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// With <paramref name="disposing"/>, it stops following the app's return to the foreground, and disposes the web view's
    /// host.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _foregroundObserver?.Dispose();
            _foregroundObserver = null;
            _host?.Dispose();
            _host = null;
        }

        base.Dispose(disposing);
    }
}
