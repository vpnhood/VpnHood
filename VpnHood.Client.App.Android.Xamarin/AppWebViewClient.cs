using System;
using Android.Webkit;
using VpnHood.Client.App.Resources;
using Xamarin.Essentials;

namespace VpnHood.Client.App.Droid;

internal class AppWebViewClient : WebViewClient
{
    public bool BrowseLinkInExternalBrowser { get; set; } = false;
    public event EventHandler? PageLoaded;

    [Obsolete("deprecated")]
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

        Browser.OpenAsync(url, options);
        return true;
    }

    // used for Window.Open such as SendReport
    public override bool ShouldOverrideUrlLoading(WebView? webView, IWebResourceRequest? request)
    {
#pragma warning disable CS0618
        return ShouldOverrideUrlLoading(webView, request?.Url?.ToString());
#pragma warning restore CS0618
    }

    public override void OnPageFinished(WebView? view, string? url)
    {
        base.OnPageFinished(view, url);
        PageLoaded?.Invoke(this, EventArgs.Empty);
    }
}