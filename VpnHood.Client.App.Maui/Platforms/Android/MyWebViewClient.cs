#nullable enable
using System;
using Android.Graphics;
using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
//using WebView = Microsoft.Maui.Controls.WebView;

namespace MauiApp3;


internal class MyWebViewClient : WebViewClient
{
    public bool BrowseLinkInExternalBrowser { get; set; } = false;
    public event EventHandler? PageLoaded;

    public MyWebViewClient()  //: base(handler)
    {
        
    }

    public override bool ShouldOverrideUrlLoading(Android.Webkit.WebView? view, string? url)
    {
        return base.ShouldOverrideUrlLoading(view, url);
    }

    public override bool ShouldOverrideUrlLoading(Android.Webkit.WebView? view, IWebResourceRequest? request)
    {
        return base.ShouldOverrideUrlLoading(view, request);
    }

    // used for Window.Open such as SendReport
    //public bool ShouldOverrideUrlLoading(WebView? webView, IWebResourceRequest? request)
    //{
    //    if (webView == null || request?.Url == null)
    //        return false;

    //    var options = new BrowserLaunchOptions
    //    {
    //        TitleMode = BrowserTitleMode.Hide,
    //        LaunchMode = BrowseLinkInExternalBrowser
    //            ? BrowserLaunchMode.External
    //            : BrowserLaunchMode.SystemPreferred
    //    };
    //    Browser. OpenAsync(request.Url.ToString(), options);
    //    return true;
    //}
    //public override void OnPageStarted(Android.Webkit.WebView? view, string? url, Bitmap? favicon)
    //{
    //    base.OnPageStarted(view, url, favicon);
    //}
    //public override WebResourceResponse? ShouldInterceptRequest(Android.Webkit.WebView? view, IWebResourceRequest? request)
    //{
    //    return base.ShouldInterceptRequest(view, request);
    //}

    //public override WebResourceResponse? ShouldInterceptRequest(Android.Webkit.WebView? view, string? url)
    //{
    //    return base.ShouldInterceptRequest(view, url);
    //}

    public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
    {
        base.OnPageFinished(view, url);
    }
}