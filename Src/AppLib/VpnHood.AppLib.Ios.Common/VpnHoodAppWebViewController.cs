using Foundation;
using UIKit;
using VpnHood.AppLib.SpaWebView;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Ios.Common;

// iOS host for the VpnHood SPA. All the hosting business logic lives in the shared SpaWebViewHost;
// this controller only supplies the iOS chrome (root-view background, status bar, safe area) and
// forwards the native lifecycle (create / foreground / dispose) to the host. The WKWebView mechanics
// live in IosSpaWebView.
public class VpnHoodAppWebViewController : UIViewController
{
    private SpaWebViewHost? _host;
    private NSObject? _foregroundObserver;

    // Edge-to-edge: paint the whole window (incl. the status-bar and home-indicator safe areas)
    // with the SPA's window background so the system bars blend into the app, matching Android.
    private UIColor BackgroundColor => GetWindowBackgroundColor() ?? UIColor.SystemBackground;

    // The SPA uses a dark window background, so the status bar should use light (white) content.
    public override UIStatusBarStyle PreferredStatusBarStyle() => UIStatusBarStyle.LightContent;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();

        View!.BackgroundColor = BackgroundColor;

        // Publish the UI context so the core/web-server can perform UI-bound operations.
        AppUiContext.Context = new IosUiContext();

        var webView = new IosSpaWebView(this, BackgroundColor);
        _host = new SpaWebViewHost(webView);
        _host.Start();

        // iOS suspends the host app in the background and can tear down the loopback socket and/or
        // jettison the WKWebView's WebContent process, with no notification. Signal resume so the host
        // re-checks the server and reloads the SPA if needed.
        _foregroundObserver = UIApplication.Notifications.ObserveWillEnterForeground(
            (_, _) => _host?.OnResume());
    }

    private static UIColor? GetWindowBackgroundColor()
    {
        var color = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor;
        if (color == null)
            return null;

        var c = color.Value;
        return UIColor.FromRGBA(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
    }

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
