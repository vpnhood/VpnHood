using Microsoft.Extensions.Logging;
using VpnHood.AppLib.SpaWebView;
using VpnHood.Core.Toolkit.Logging;
using Uri = System.Uri;

namespace VpnHood.AppLib.Maui.Common;

// MAUI ISpaWebView adapter: the only MAUI-specific SPA-hosting code. It wraps a
// Microsoft.Maui.Controls.WebView and reports its Navigated event to SpaWebViewHost. MAUI has no
// content-process-terminated signal, so ContentProcessGone is never raised.
public sealed class MauiSpaWebView(WebView webView, IDispatcher dispatcher,
    ActivityIndicator? spinner = null, Label? errorLabel = null) : ISpaWebView
{
    public event EventHandler? PageLoaded;
    public event EventHandler? LoadFailed;

#pragma warning disable CS0067 // never raised on MAUI, see above
    public event EventHandler? ContentProcessGone;
#pragma warning restore CS0067

    public void Initialize()
    {
        webView.Navigated += OnNavigated;
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
                LoadFailed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public void Load(Uri url)
    {
        webView.Source = new UrlWebViewSource { Url = url.ToString() };
    }

    public void SetLoading(bool isLoading)
    {
        if (spinner == null)
            return;

        spinner.IsVisible = isLoading;
        spinner.IsRunning = isLoading;
    }

    public void ShowError(string message)
    {
        if (spinner != null) {
            spinner.IsVisible = false;
            spinner.IsRunning = false;
        }

        if (errorLabel != null) {
            errorLabel.Text = "Failed to start the user interface.\n\n" + message;
            errorLabel.IsVisible = true;
        }
    }

    public void Post(Action action)
    {
        dispatcher.Dispatch(action);
    }
}
