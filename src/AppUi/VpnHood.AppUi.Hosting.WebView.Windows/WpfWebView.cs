using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VpnHood.AppUi.Hosting.WebView;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.AppLib.App.Windows;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// Windows (WPF/WebView2) IWebView adapter: the only WebView2-specific SPA-hosting code. When the
// Edge WebView2 runtime is unavailable it invokes the window-provided fallback (open the SPA in the
// system browser).
public sealed class WpfWebView(WebView2 webView, Action onWebView2Unavailable) : IWebView
{
    private Uri? _pendingUrl;

    public event EventHandler? PageLoaded;
    public event EventHandler? LoadFailed;
    public event EventHandler? ContentProcessGone;

    public void Initialize()
    {
        webView.CoreWebView2InitializationCompleted += OnCoreInitCompleted;
        _ = webView.EnsureCoreWebView2Async(null);
    }

    private void OnCoreInitCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess) {
            // Edge WebView2 runtime missing / failed to initialize — fall back to the external browser.
            VhLogger.Instance.LogError(e.InitializationException, "WebView2 initialization failed.");
            onWebView2Unavailable();
            return;
        }

        webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        webView.CoreWebView2.ProcessFailed += OnProcessFailed;

        if (_pendingUrl != null) {
            webView.CoreWebView2.Navigate(_pendingUrl.ToString());
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
        if (e.IsSuccess) {
            PageLoaded?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Only connection-level failures mean the loopback server is unreachable. OperationCanceled
        // is our own superseding Navigate (the WebView2 twin of iOS NSUrlError.Cancelled) and must
        // not trigger a reload, or the reload would keep cancelling itself.
        VhLogger.Instance.LogWarning("WebView2 navigation failed: {Status}", e.WebErrorStatus);
        if (e.WebErrorStatus is CoreWebView2WebErrorStatus.ServerUnreachable
            or CoreWebView2WebErrorStatus.Timeout
            or CoreWebView2WebErrorStatus.ConnectionAborted
            or CoreWebView2WebErrorStatus.ConnectionReset
            or CoreWebView2WebErrorStatus.Disconnected
            or CoreWebView2WebErrorStatus.CannotConnect
            or CoreWebView2WebErrorStatus.HostNameNotResolved)
            LoadFailed?.Invoke(this, EventArgs.Empty);
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        VhLogger.Instance.LogWarning("WebView2 process failed: {Kind}", e.ProcessFailedKind);
        ContentProcessGone?.Invoke(this, EventArgs.Empty);
    }

    public void Load(Uri url)
    {
        if (webView.CoreWebView2 != null)
            webView.CoreWebView2.Navigate(url.ToString());
        else
            _pendingUrl = url; // navigate once CoreWebView2 finishes initializing
    }

    public void SetLoading(bool isLoading)
    {
        // The WPF window shows no separate loading indicator.
    }

    public void ShowError(string message)
    {
        VhLogger.Instance.LogError("SPA host error: {Message}", message);
        onWebView2Unavailable();
    }

    public void Post(Action action)
    {
        webView.Dispatcher.Invoke(action);
    }
}
