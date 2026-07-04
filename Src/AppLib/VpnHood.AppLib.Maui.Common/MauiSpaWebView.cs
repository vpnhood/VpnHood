using Microsoft.Extensions.Logging;
using VpnHood.AppLib.SpaWebView;
using VpnHood.Core.Toolkit.Logging;
using Uri = System.Uri;

namespace VpnHood.AppLib.Maui.Common;

// MAUI ISpaWebView adapter: the only MAUI-specific SPA-hosting code. It wraps a
// Microsoft.Maui.Controls.WebView and maps its Navigated event onto the platform-neutral
// SpaWebViewHost events. MAUI has no content-process-terminated signal, so ContentProcessGone is
// never raised; recovery still comes from the server health monitor + resume + Restarted reload.
public sealed class MauiSpaWebView(WebView webView, IDispatcher dispatcher,
    ActivityIndicator? spinner = null, Label? errorLabel = null) : ISpaWebView
{
    public event EventHandler? PageLoaded;
    public event EventHandler<SpaLoadFailedEventArgs>? LoadFailed;

    // MAUI has no content-process-terminated signal, so ContentProcessGone is never raised (recovery
    // comes from the health monitor + resume + Restarted). Suppress the "never used" warning.
#pragma warning disable CS0067
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
                LoadFailed?.Invoke(this, new SpaLoadFailedEventArgs(duringInitialConnect: true));
                break;
        }
    }

    public void Load(Uri url)
    {
        webView.Source = new UrlWebViewSource { Url = url.ToString() };
    }

    public void Reload()
    {
        webView.Reload();
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
