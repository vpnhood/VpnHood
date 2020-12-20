using System;
using Android.OS;
using Android.Webkit;
using Xamarin.Essentials;

namespace VpnHood.Client.App.Android
{
    class MyWebChromeClient : WebChromeClient
    {
        public bool BrowseLinkInExternalBrowser { get; set; } = true;

        public override bool OnCreateWindow(WebView view, bool isDialog, bool isUserGesture, Message resultMsg)
        {
            using var href = view.Handler.ObtainMessage();

            view.RequestFocusNodeHref(href);
            var url = href.Data?.GetString("url");
            if (url == null)
                return false;

            Browser.OpenAsync(new Uri(url), BrowseLinkInExternalBrowser ? BrowserLaunchMode.External : BrowserLaunchMode.SystemPreferred);
            resultMsg.SendToTarget();
            return true;
        }

    }
}