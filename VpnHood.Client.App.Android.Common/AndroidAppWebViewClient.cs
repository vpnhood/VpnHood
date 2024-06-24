using Android.Content;
using Android.Webkit;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Client.App.Droid.Common;

internal class AndroidAppWebViewClient : WebViewClient
{
    public event EventHandler? PageLoaded;
    private string? _mainHost;

    private bool IsMainHost(string? url)
    {
        return _mainHost != null && url != null && _mainHost.Equals(new Uri(url).Host, StringComparison.OrdinalIgnoreCase);
    }

    public override bool ShouldOverrideUrlLoading(WebView? webView, string? url)
    {
        if (webView == null || url == null || IsMainHost(url))
            return false;

        // ignore root
        var uri = new Uri(url);
        if (uri.AbsolutePath == "/" || string.IsNullOrEmpty(uri.AbsolutePath))
            return false;

        try
        {
            var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
            intent.SetFlags(ActivityFlags.NewTask);
            Application.Context.StartActivity(intent);
        }
        catch (Exception ex)
        {
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