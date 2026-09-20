using Microsoft.Extensions.Logging;
using VpnHood.AppLib;
using VpnHood.AppLib.Ios.Common;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.AppUi.Hosting.WebView;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppUi.Hosting.WebView.Ios;

// iOS host for the VpnHood SPA. All the hosting business logic lives in the shared WebViewHost;
// this controller only supplies the iOS chrome (root-view background, status bar, safe area) and
// forwards the native lifecycle (create / foreground / dispose) to the host. The WKWebView mechanics
// live in IosWebView.
public class IosWebViewController : UIViewController
{
    private WebViewHost? _host;
    private NSObject? _foregroundObserver;

    // Rotation is opt-in: the SPA is portrait-first, so the controller locks to portrait unless the
    // implementor enables rotation. On iPad the mask is only honored when the host app also sets
    // UIRequiresFullScreen in its Info.plist (multitasking-capable iPad apps ignore it).
    public bool AllowRotation { get; init; }

    public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations() =>
        AllowRotation ? UIInterfaceOrientationMask.All : UIInterfaceOrientationMask.Portrait;

    // Edge-to-edge: paint the whole window (incl. the status-bar and home-indicator safe areas)
    // with the SPA's window background so the system bars blend into the app, matching Android.
    private static UIColor BackgroundColor => GetWindowBackgroundColor() ?? UIColor.SystemBackground;

    // The SPA uses a dark window background, so the status bar should use light (white) content.
    public override UIStatusBarStyle PreferredStatusBarStyle() => UIStatusBarStyle.LightContent;

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
            await VpnHoodApp.Instance.ResourcesLoaded;
            View!.BackgroundColor = BackgroundColor;

            var webView = new IosWebView(this, BackgroundColor);
            _host = new WebViewHost(webView);
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
