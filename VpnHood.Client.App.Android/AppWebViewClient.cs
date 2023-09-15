using Android.Content;
using Android.Webkit;
using VpnHood.Client.App.Resources;
using Xamarin.Essentials;
using Browser = Android.Provider.Browser;

namespace VpnHood.Client.App.Droid;

internal class AppWebViewClient : WebViewClient
{
    public bool BrowseLinkInExternalBrowser { get; set; } = false;
    public event EventHandler? PageLoaded;

    public override bool ShouldOverrideUrlLoading(WebView? webView, string? url)
    {
        if (webView == null || url == null)
            return false;

        // var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
        // intent.SetFlags(ActivityFlags.NewTask);
        // Application.Context.StartActivity(intent);

        var options = new BrowserLaunchOptions
        {
            TitleMode = BrowserTitleMode.Hide,
            PreferredToolbarColor = UiDefaults.WindowBackgroundColor,
            LaunchMode = BrowseLinkInExternalBrowser
                ? BrowserLaunchMode.External
                : BrowserLaunchMode.SystemPreferred
        };

        Xamarin.Essentials.Browser.OpenAsync(url, options);
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