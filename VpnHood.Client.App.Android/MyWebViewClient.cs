using Android.Graphics;
using Android.Webkit;
using System;
using Xamarin.Essentials;

namespace VpnHood.Client.App.Android
{
    class MyWebViewClient : WebViewClient
    {
        public event EventHandler PageLoaded;
        public override bool ShouldOverrideUrlLoading(WebView webView, IWebResourceRequest request)
        {
            Browser.OpenAsync(request.Url.ToString(), BrowserLaunchMode.External);
            //return base.ShouldOverrideUrlLoading(webView, request);
            return true;
        }

        public override void OnPageFinished(WebView view, string url)
        {
            base.OnPageFinished(view, url);
            PageLoaded?.Invoke(this, EventArgs.Empty);

        }
    }
}