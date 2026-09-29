using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VpnHood.AppLib.Api.App;
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
    private string? _localToken;

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

    // A loopback link - the log - asks for the token, which no browser can send, so it opens in a
    // window of its own, loaded with the header. Any other link goes to the system browser.
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        var uri = new Uri(e.Uri);
        if (uri.IsLoopback)
            _ = ShowLoopbackWindow(uri);
        else
            WindowsShell.OpenUrl(uri);
    }

    private async Task ShowLoopbackWindow(Uri uri)
    {
        try {
            var view = new WebView2();
            var window = new Window {
                Title = Path.GetFileName(uri.LocalPath),
                Width = 900,
                Height = 700,
                Owner = Window.GetWindow(webView),
                Content = view
            };
            window.Closed += (_, _) => view.Dispose();
            window.Show();

            // the page's own environment, so this window keeps to the same user-data folder
            await view.EnsureCoreWebView2Async(webView.CoreWebView2.Environment);
            var headers = _localToken != null ? $"Authorization: Bearer {_localToken}" : "";
            view.CoreWebView2.NavigateWithWebResourceRequest(
                view.CoreWebView2.Environment.CreateWebResourceRequest(uri.ToString(), "GET", null, headers));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not open a loopback link in a window of its own.");
        }
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
        // the token the page reads after "#", which a loopback link it opens needs too
        _localToken = LocalApiToken.Read(url);
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
