using Android.OS;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WebView = Android.Webkit.WebView;

namespace VpnHood.Client.App.Maui;

public class AppWebChromeClient : MauiWebChromeClient
{
    private readonly WebViewHandler _webViewHandler;

    public override bool OnCreateWindow(WebView? webView, bool isDialog, bool isUserGesture, Message? resultMsg)
    {
        if (webView?.Context == null)
            return false;

        var newWebView = new WebView(webView.Context);
        newWebView.SetWebViewClient(new AppWebViewClient(_webViewHandler));
        newWebView.SetWebChromeClient(this);

        if (resultMsg?.Obj is not WebView.WebViewTransport transport)
            return false;

        transport.WebView = newWebView;
        resultMsg.SendToTarget();
        return true;
    }

    public AppWebChromeClient(WebViewHandler handler) 
        : base(handler)
    {
        _webViewHandler = handler;
    }
}