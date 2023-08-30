using Android.OS;
using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace VpnHood.Client.App.Maui;

public class MyWebChromeClient : WebChromeClient
{
    public MyWebChromeClient(IWebViewHandler handler) 
    {

    }

    public override bool OnCreateWindow(Android.Webkit.WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
    {
        return base.OnCreateWindow(view, isDialog, isUserGesture, resultMsg);
        //if (view?.Context == null) return false;
        //var newWebView = new WebView(view.Context);
        //newWebView.SetWebViewClient(new MyWebViewClient());

        //if (resultMsg?.Obj is not WebView.WebViewTransport transport) return false;
        //transport.WebView = newWebView;
        //resultMsg.SendToTarget();
        //return true;
    }
}