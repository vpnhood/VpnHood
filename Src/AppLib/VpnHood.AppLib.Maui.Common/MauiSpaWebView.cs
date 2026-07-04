using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using VpnHood.AppLib.SpaWebView;
using VpnHood.Core.Toolkit.Logging;
using Uri = System.Uri;

namespace VpnHood.AppLib.Maui.Common;

// MAUI ISpaWebView adapter: the only MAUI-specific SPA-hosting code. It wraps a
// Microsoft.Maui.Controls.WebView and maps its Navigated event onto the platform-neutral
// SpaWebViewHost events. MAUI has no content-process-terminated signal, so ContentProcessGone is
// never raised; recovery still comes from the server health monitor + resume + Restarted reload.
public sealed class MauiSpaWebView : ISpaWebView
{
    private readonly WebView _webView;
    private readonly IDispatcher _dispatcher;
    private readonly ActivityIndicator? _spinner;
    private readonly Label? _errorLabel;

    public event EventHandler? PageLoaded;
    public event EventHandler<SpaLoadFailedEventArgs>? LoadFailed;
    public event EventHandler? ContentProcessGone;

    public MauiSpaWebView(WebView webView, IDispatcher dispatcher,
        ActivityIndicator? spinner = null, Label? errorLabel = null)
    {
        _webView = webView;
        _dispatcher = dispatcher;
        _spinner = spinner;
        _errorLabel = errorLabel;
    }

    public void Initialize()
    {
        _webView.Navigated += OnNavigated;
    }

    private void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        switch (e.Result) {
            case WebNavigationResult.Success:
                PageLoaded?.Invoke(this, EventArgs.Empty);
                break;

            case WebNavigationResult.Cancel:
                // Superseded / cancelled load (e.g. our own reload) — not a failure.
                break;

            default:
                VhLogger.Instance.LogWarning("MAUI WebView navigation failed: {Result}", e.Result);
                LoadFailed?.Invoke(this, new SpaLoadFailedEventArgs(duringInitialConnect: true));
                break;
        }
    }

    public void Load(Uri url)
    {
        _webView.Source = new UrlWebViewSource { Url = url.ToString() };
    }

    public void Reload()
    {
        _webView.Reload();
    }

    public void SetLoading(bool isLoading)
    {
        if (_spinner == null)
            return;

        _spinner.IsVisible = isLoading;
        _spinner.IsRunning = isLoading;
    }

    public void ShowError(string message)
    {
        if (_spinner != null) {
            _spinner.IsVisible = false;
            _spinner.IsRunning = false;
        }

        if (_errorLabel != null) {
            _errorLabel.Text = "Failed to start the user interface.\n\n" + message;
            _errorLabel.IsVisible = true;
        }
    }

    public void Post(Action action)
    {
        _dispatcher.Dispatch(action);
    }
}
