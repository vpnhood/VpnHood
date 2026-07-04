using Foundation;
using Microsoft.Extensions.Logging;
using UIKit;
using VpnHood.AppLib.SpaWebView;
using VpnHood.Core.Toolkit.Logging;
using WebKit;

namespace VpnHood.AppLib.Ios.Common;

// iOS ISpaWebView adapter: the only iOS-specific SPA-hosting code. It owns a WKWebView (plus a
// loading spinner and an error label) inside the given UIViewController, and maps the
// WKNavigationDelegate callbacks onto the platform-neutral events SpaWebViewHost drives.
public sealed class IosSpaWebView : ISpaWebView
{
    private readonly UIViewController _controller;
    private readonly UIColor _backgroundColor;
    private WKWebView? _webView;
    private UIActivityIndicatorView? _spinner;

    public event EventHandler? PageLoaded;
    public event EventHandler<SpaLoadFailedEventArgs>? LoadFailed;
    public event EventHandler? ContentProcessGone;

    public IosSpaWebView(UIViewController controller, UIColor backgroundColor)
    {
        _controller = controller;
        _backgroundColor = backgroundColor;

        // Loading indicator, centered and shown immediately (SPA zip extraction + socket bind happen
        // before the first navigation). Hidden automatically once stopped.
        _spinner = new UIActivityIndicatorView(UIActivityIndicatorViewStyle.Large) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            HidesWhenStopped = true
        };
        var view = _controller.View!;
        view.AddSubview(_spinner);
        NSLayoutConstraint.ActivateConstraints([
            _spinner.CenterXAnchor.ConstraintEqualTo(view.CenterXAnchor),
            _spinner.CenterYAnchor.ConstraintEqualTo(view.CenterYAnchor)
        ]);
        _spinner.StartAnimating();
    }

    public void Initialize()
    {
        var config = new WKWebViewConfiguration();
        config.AllowsInlineMediaPlayback = true;
        config.DefaultWebpagePreferences.AllowsContentJavaScript = true;
        // Autoplay/JS-opened windows without a user gesture (parity with the Android WebView).
        config.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None;

        var view = _controller.View!;
        _webView = new WKWebView(view.Bounds, config) {
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
            BackgroundColor = _backgroundColor,
            Opaque = false,
            NavigationDelegate = new NavDelegate(this)
        };

        // The status-bar / home-indicator safe-area gaps and any over-scroll area should show the
        // app background color (not white), so the system bars blend into the app like Android.
        _webView.ScrollView.BackgroundColor = _backgroundColor;

        // True edge-to-edge: by default WKWebView auto-insets content for the safe area, which would
        // double up with the SPA's own SystemBarsInfo padding. Disable the automatic inset so the page
        // fills the whole window and ONLY the SPA pads itself — matching Android's edge-to-edge.
        _webView.ScrollView.ContentInsetAdjustmentBehavior =
            UIScrollViewContentInsetAdjustmentBehavior.Never;

        // Allow Safari Web Inspector to attach when debugging (iOS 16.4+).
        if (VpnHoodApp.Instance.Features.IsDebugMode && OperatingSystem.IsIOSVersionAtLeast(16, 4))
            _webView.Inspectable = true;

        // Insert below the spinner so the loading indicator stays visible until the first page loads.
        view.InsertSubview(_webView, 0);
    }

    public void Load(Uri url)
    {
        _webView?.LoadRequest(new NSUrlRequest(new NSUrl(url.ToString())));
    }

    public void Reload()
    {
        _webView?.Reload();
    }

    public void SetLoading(bool isLoading)
    {
        if (isLoading)
            _spinner?.StartAnimating();
        else
            _spinner?.StopAnimating();
    }

    public void ShowError(string message)
    {
        _spinner?.StopAnimating();

        var view = _controller.View!;
        var label = new UILabel {
            Text = "Failed to start the user interface.\n\n" + message,
            Lines = 0,
            TextAlignment = UITextAlignment.Center,
            TextColor = UIColor.Label,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        view.AddSubview(label);
        NSLayoutConstraint.ActivateConstraints([
            label.CenterXAnchor.ConstraintEqualTo(view.CenterXAnchor),
            label.CenterYAnchor.ConstraintEqualTo(view.CenterYAnchor),
            label.LeadingAnchor.ConstraintEqualTo(view.SafeAreaLayoutGuide.LeadingAnchor, 24),
            label.TrailingAnchor.ConstraintEqualTo(view.SafeAreaLayoutGuide.TrailingAnchor, -24)
        ]);
    }

    public void Post(Action action)
    {
        _controller.BeginInvokeOnMainThread(action);
    }

    private void RaisePageLoaded() => PageLoaded?.Invoke(this, EventArgs.Empty);
    private void RaiseLoadFailed(bool duringInitialConnect) =>
        LoadFailed?.Invoke(this, new SpaLoadFailedEventArgs(duringInitialConnect));
    private void RaiseContentProcessGone() => ContentProcessGone?.Invoke(this, EventArgs.Empty);

    private sealed class NavDelegate(IosSpaWebView owner) : WKNavigationDelegate
    {
        public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
        {
            owner.RaisePageLoaded();
        }

        public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        {
            // Failure of an already-committed navigation — not a server-connect failure, so just
            // clear the loading state rather than triggering a server restart.
            VhLogger.Instance.LogWarning("WebView navigation failed: {Error}", error.LocalizedDescription);
            owner.RaisePageLoaded();
        }

        public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation,
            NSError error)
        {
            // NSURLErrorCancelled (-999) is the expected result of superseding an in-flight load
            // (e.g. our own reload) — it is not a server failure, so don't report it or recovery
            // would perpetually cancel itself into the retry cap.
            const int nsUrlErrorCancelled = -999;
            if (error.Code == nsUrlErrorCancelled)
                return;

            VhLogger.Instance.LogWarning("WebView provisional navigation failed: {Error}",
                error.LocalizedDescription);
            owner.RaiseLoadFailed(duringInitialConnect: true);
        }

        // iOS jettisoned the WebView's content process under memory pressure — the page is now blank.
        public override void ContentProcessDidTerminate(WKWebView webView)
        {
            VhLogger.Instance.LogWarning("WKWebView content process terminated; recovering.");
            owner.RaiseContentProcessGone();
        }
    }
}
