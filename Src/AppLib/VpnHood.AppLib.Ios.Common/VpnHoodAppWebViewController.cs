using Foundation;
using Microsoft.Extensions.Logging;
using UIKit;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Logging;
using WebKit;

namespace VpnHood.AppLib.Ios.Common;

// iOS counterpart of AndroidAppWebViewMainActivityHandler. Hosts the VpnHood SPA inside a
// WKWebView that loads from the in-process VpnHoodAppWebServer (a loopback HTTP server serving
// the embedded SPA zip from AppResources.SpaZipData).
public class VpnHoodAppWebViewController : UIViewController
{
    private WKWebView? _webView;
    private UIActivityIndicatorView? _spinner;

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

        // Loading indicator shown until the SPA finishes its first navigation.
        _spinner = new UIActivityIndicatorView(UIActivityIndicatorViewStyle.Large) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            HidesWhenStopped = true
        };
        View.AddSubview(_spinner);
        NSLayoutConstraint.ActivateConstraints([
            _spinner.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor),
            _spinner.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor)
        ]);
        _spinner.StartAnimating();

        // Start the web server off the main thread (it extracts the SPA zip and binds a socket),
        // then build the WebView on the main thread.
        Task.Run(InitWebServerAndUi);
    }

    private void InitWebServerAndUi()
    {
        try {
            if (!VpnHoodAppWebServer.IsInit)
                VpnHoodAppWebServer.Init();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to initialize the SPA web server.");
            BeginInvokeOnMainThread(() => ShowError(ex));
            return;
        }

        BeginInvokeOnMainThread(InitWebUi);
    }

    private void InitWebUi()
    {
        try {
            var config = new WKWebViewConfiguration();
            config.AllowsInlineMediaPlayback = true;
            config.DefaultWebpagePreferences.AllowsContentJavaScript = true;
            // Autoplay/JS-opened windows without a user gesture (parity with the Android WebView).
            config.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None;

            _webView = new WKWebView(View!.Bounds, config) {
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
                BackgroundColor = BackgroundColor,
                Opaque = false,
                NavigationDelegate = new NavDelegate(this)
            };

            // The status-bar / home-indicator safe-area gaps and any over-scroll area should show the
            // app background color (not white), so the system bars blend into the app like Android.
            _webView.ScrollView.BackgroundColor = BackgroundColor;

            // Allow Safari Web Inspector to attach when debugging (iOS 16.4+).
            if (VpnHoodApp.Instance.Features.IsDebugMode && OperatingSystem.IsIOSVersionAtLeast(16, 4))
                _webView.Inspectable = true;

            View.InsertSubview(_webView, 0);

            var launchUrl = GetLaunchUrl();
            VhLogger.Instance.LogInformation("Loading SPA into WebView. Url={Url}", launchUrl);
            _webView.LoadRequest(new NSUrlRequest(new NSUrl(launchUrl)));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to create the WebView.");
            ShowError(ex);
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

    private static string GetLaunchUrl()
    {
        // nocache busts the WebView cache whenever the bundled SPA changes.
        return $"{VpnHoodAppWebServer.Instance.Url}?nocache={VpnHoodAppWebServer.Instance.SpaHash}";
    }

    private void ShowError(Exception ex)
    {
        _spinner?.StopAnimating();

        var label = new UILabel {
            Text = "Failed to start the user interface.\n\n" + ex.Message,
            Lines = 0,
            TextAlignment = UITextAlignment.Center,
            TextColor = UIColor.Label,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        View!.AddSubview(label);
        NSLayoutConstraint.ActivateConstraints([
            label.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor),
            label.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor),
            label.LeadingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.LeadingAnchor, 24),
            label.TrailingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TrailingAnchor, -24)
        ]);
    }

    private void OnPageLoaded()
    {
        _spinner?.StopAnimating();
    }

    // Hides the spinner once the SPA has rendered its first page.
    private sealed class NavDelegate(VpnHoodAppWebViewController owner) : WKNavigationDelegate
    {
        public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
        {
            owner.OnPageLoaded();
        }

        public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        {
            VhLogger.Instance.LogWarning("WebView navigation failed: {Error}", error.LocalizedDescription);
            owner.OnPageLoaded();
        }
    }
}
