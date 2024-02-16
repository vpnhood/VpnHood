using Android.Content;
using Android.Webkit;
using Uri = Android.Net.Uri;

namespace VpnHood.Client.App.Droid.Common;

internal class AndroidAppWebViewClient : WebViewClient
{
    public event EventHandler? PageLoaded;

    public override bool ShouldOverrideUrlLoading(WebView? webView, string? url)
    {
        if (webView == null || url == null)
            return false;

        var intent = new Intent(Intent.ActionView, Uri.Parse(url));
        intent.SetFlags(ActivityFlags.NewTask);
        Application.Context.StartActivity(intent);

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
        PageLoaded?.Invoke(this, EventArgs.Empty);
    }
}