using Android.OS;
using Android.Webkit;
using System;
using Xamarin.Essentials;

namespace VpnHood.Client.App.Android
{
    class MyWebChromeClient : WebChromeClient
    {
        public override bool OnCreateWindow(WebView view, bool isDialog, bool isUserGesture, Message resultMsg)
        {
            var newWebView = new WebView(view.Context);
            newWebView.SetWebViewClient(new MyWebViewClient());
            var transport = (WebView.WebViewTransport)resultMsg.Obj;
            transport.WebView = newWebView;
            resultMsg.SendToTarget();
            return true;
        }
    }
}