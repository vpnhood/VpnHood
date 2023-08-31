using Android.OS;
using Android.Webkit;
using Java.Net;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WebView = Android.Webkit.WebView;

namespace VpnHood.Client.App.Maui;

public class MyWebChromeClient : MauiWebChromeClient
{
    private readonly WebViewHandler _webViewHandler;

    public override bool OnCreateWindow(WebView? webView, bool isDialog, bool isUserGesture, Message? resultMsg)
    {
        if (webView?.Context == null)
            return false;

        var newWebView = new WebView(webView.Context);
        newWebView.SetWebViewClient(new MyWebViewClient(_webViewHandler));
        newWebView.SetWebChromeClient(this);

        if (resultMsg?.Obj is not WebView.WebViewTransport transport)
            return false;

        transport.WebView = newWebView;
        resultMsg.SendToTarget();
        return true;
    }

    public MyWebChromeClient(WebViewHandler handler) 
        : base(handler)
    {
        _webViewHandler = handler;
    }
}