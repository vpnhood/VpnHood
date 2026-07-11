using Android.Content;
using Android.Graphics;
using Android.Webkit;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.SpaWebView;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Droid.Common.SpaWebView;

internal class AndroidSpaWebViewClient : WebViewClient
{
    public event EventHandler? PageLoaded;
    public event EventHandler<SpaLoadFailedEventArgs>? LoadFailed;
    public event EventHandler? RenderProcessGone;
    private string? _mainHost;
    private bool _mainFrameConnectFailed;

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

    public override void OnPageStarted(WebView? view, string? url, Bitmap? favicon)
    {
        base.OnPageStarted(view, url, favicon);
        _mainFrameConnectFailed = false; // new navigation, new verdict
    }

    public override void OnReceivedError(WebView? view, IWebResourceRequest? request, WebResourceError? error)
    {
        base.OnReceivedError(view, request, error);

        // Sub-resource failures are the SPA's own business; only the main document matters here.
        if (request?.IsForMainFrame != true || error == null)
            return;

        VhLogger.Instance.LogWarning("SPA web view main-frame load failed: {ErrorCode} {Description}",
            error.ErrorCode, error.Description);

        // Only connection-level failures mean the loopback server is unreachable. Anything else —
        // notably ERR_ABORTED (reported as ClientError.Unknown) from our own superseding LoadUrl —
        // must not trigger recovery, or recovery would perpetually cancel itself into the retry cap
        // (the Android twin of iOS ignoring NSUrlError.Cancelled).
        if (error.ErrorCode is not (ClientError.Connect or ClientError.Timeout or ClientError.HostLookup
            or ClientError.Io))
            return;

        // Chromium renders its own error page for this navigation and OnPageFinished still fires for
        // it, so flag the navigation to keep that from being reported as a successful load.
        _mainFrameConnectFailed = true;
        LoadFailed?.Invoke(this, new SpaLoadFailedEventArgs(duringInitialConnect: true));
    }

    // The WebView's render process died (commonly OOM-killed while the app was minimized). Returning
    // true claims the recovery — returning false (the default) makes Android kill the whole app.
    public override bool OnRenderProcessGone(WebView? view, RenderProcessGoneDetail? detail)
    {
        VhLogger.Instance.LogWarning("SPA web view render process is gone.");
        RenderProcessGone?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public override void OnPageFinished(WebView? view, string? url)
    {
        base.OnPageFinished(view, url);

        // A failed main-frame navigation still "finishes" (Chromium's error page); reporting it as
        // loaded would make the host hide the loader and reset its recovery state on a dead page.
        if (_mainFrameConnectFailed)
            return;

        _mainHost ??= url != null ? new Uri(url).Host : null;
        PageLoaded?.Invoke(this, EventArgs.Empty);
    }
}