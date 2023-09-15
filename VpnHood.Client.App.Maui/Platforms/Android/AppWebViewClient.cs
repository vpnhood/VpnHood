using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WebView = Android.Webkit.WebView;

namespace VpnHood.Client.App.Maui;
internal class AppWebViewClient : MauiWebViewClient
{
    public bool BrowseLinkInExternalBrowser { get; set; } = false;

    public AppWebViewClient(WebViewHandler handler) 
        : base(handler)
    {
    }

    public override bool ShouldOverrideUrlLoading(WebView? webView, string? url)
    {
        if (webView == null || url == null)
            return false;

        var options = new BrowserLaunchOptions
        {
            TitleMode = BrowserTitleMode.Hide,
            LaunchMode = BrowseLinkInExternalBrowser
                ? BrowserLaunchMode.External
                : BrowserLaunchMode.SystemPreferred
        };

        // covert to ThemePrimaryColor to Android Color
        if (App.Current?.BackgroundColor != null)
            options.PreferredToolbarColor = App.Current.BackgroundColor;

        Browser.OpenAsync(url, options);
        return true;
    }

    public override bool ShouldOverrideUrlLoading(WebView? webView, IWebResourceRequest? request)
    {
        return ShouldOverrideUrlLoading(webView, request?.Url?.ToString());
    }
}