using Android.Content;
using Android.Webkit;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Droid.Common.SpaWebView;

internal class AndroidSpaWebViewClient : WebViewClient
{
    public event EventHandler? PageLoaded;
    private string? _mainHost;

    private bool IsMainHost(string? url)
    {
        return _mainHost != null && url != null &&
               _mainHost.Equals(new Uri(url).Host, StringComparison.OrdinalIgnoreCase);
    }

    public override bool ShouldOverrideUrlLoading(WebView? webView, string? url)
    {
        if (webView == null || url == null || IsMainHost(url))
            return false;

        // The SPA opens the log/report via window.open at the on-device loopback server. Show it in an in-app
        // viewer (with find + share) instead of an external browser, which can't reach the loopback server and
        // kicks the user out of the app. Non-loopback links are real external links → system browser.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback &&
            webView.Context is Activity activity) {
            AndroidReportViewer.Show(activity, uri);
            return true;
        }

        try {
            var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
            intent.SetFlags(ActivityFlags.NewTask);
            Application.Context.StartActivity(intent);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, $"Could not launch any activity for {url}");
        }

        return true;
    }

    // used for Window.Open such as SendReport
    public override bool ShouldOverrideUrlLoading(WebView? webView, IWebResourceRequest? request)
    {
        return ShouldOverrideUrlLoading(webView, request?.Url?.ToString());
    }

    public override void OnPageFinished(WebView? view, string? url)
    {
        base.OnPageFinished(view, url);
        _mainHost ??= url != null ? new Uri(url).Host : null;
        PageLoaded?.Invoke(this, EventArgs.Empty);
    }
}