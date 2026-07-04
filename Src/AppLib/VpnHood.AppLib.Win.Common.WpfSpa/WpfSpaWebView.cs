using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VpnHood.AppLib.SpaWebView;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Win.Common.WpfSpa;

// Windows (WPF/WebView2) ISpaWebView adapter: the only WebView2-specific SPA-hosting code. It maps
// the WebView2 events onto the platform-neutral SpaWebViewHost events and, when the Edge WebView2
// runtime is unavailable, invokes the window-provided fallback (open the SPA in the system browser).
public sealed class WpfSpaWebView : ISpaWebView
{
    private readonly WebView2 _webView;
    private readonly Dispatcher _dispatcher;
    private readonly Action _onWebView2Unavailable;
    private Uri? _pendingUrl;

    public event EventHandler? PageLoaded;
    public event EventHandler<SpaLoadFailedEventArgs>? LoadFailed;
    public event EventHandler? ContentProcessGone;

    public WpfSpaWebView(WebView2 webView, Action onWebView2Unavailable)
    {
        _webView = webView;
        _dispatcher = webView.Dispatcher;
        _onWebView2Unavailable = onWebView2Unavailable;
    }

    public void Initialize()
    {
        _webView.CoreWebView2InitializationCompleted += OnCoreInitCompleted;
        _ = _webView.EnsureCoreWebView2Async(null);
    }

    private void OnCoreInitCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess) {
            // Edge WebView2 runtime missing / failed to initialize — fall back to the external browser.
            VhLogger.Instance.LogError(e.InitializationException, "WebView2 initialization failed.");
            _onWebView2Unavailable();
            return;
        }

        _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _webView.CoreWebView2.ProcessFailed += OnProcessFailed;

        if (_pendingUrl != null) {
            _webView.CoreWebView2.Navigate(_pendingUrl.ToString());
            _pendingUrl = null;
        }
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        VpnHoodAppWin.OpenUrlInExternalBrowser(new Uri(e.Uri));
        e.Handled = true;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
            PageLoaded?.Invoke(this, EventArgs.Empty);
        // A WebView2 navigation failure on desktop is typically not a dead loopback listener (unlike
        // iOS backgrounding), so we don't force a server restart here — the 1s health monitor covers a
        // genuinely dead server.
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        VhLogger.Instance.LogWarning("WebView2 process failed: {Kind}", e.ProcessFailedKind);
        ContentProcessGone?.Invoke(this, EventArgs.Empty);
    }

    public void Load(Uri url)
    {
        if (_webView.CoreWebView2 != null)
            _webView.CoreWebView2.Navigate(url.ToString());
        else
            _pendingUrl = url; // navigate once CoreWebView2 finishes initializing
    }

    public void Reload()
    {
        _webView.CoreWebView2?.Reload();
    }

    public void SetLoading(bool isLoading)
    {
        // The WPF window shows no separate loading indicator.
    }

    public void ShowError(string message)
    {
        VhLogger.Instance.LogError("SPA host error: {Message}", message);
        _onWebView2Unavailable();
    }

    public void Post(Action action)
    {
        _dispatcher.Invoke(action);
    }
}
