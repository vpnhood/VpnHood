using Android.Content;
using Android.Webkit;

namespace VpnHood.Client.App.Droid.Common;

internal class AndroidAppWebViewClient : WebViewClient
{
    public bool BrowseLinkInExternalBrowser { get; set; } = false;
    public event EventHandler? PageLoaded;

    public override bool ShouldOverrideUrlLoading(WebView? webView, string? url)
    {
        if (webView == null || url == null)
            return false;

        var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
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