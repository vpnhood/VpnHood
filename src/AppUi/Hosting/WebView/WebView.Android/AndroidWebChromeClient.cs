using Android.OS;
using Android.Webkit;

using NativeWebView = Android.Webkit.WebView;

namespace VpnHood.AppUi.Hosting.WebView.Droid;

internal class AndroidWebChromeClient : WebChromeClient
{
    public override bool OnCreateWindow(NativeWebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
    {
        if (view?.Context == null)
            return false;

        var newWebView = new NativeWebView(view.Context);
        newWebView.SetWebViewClient(new AndroidWebViewClient());
        if (resultMsg?.Obj is not NativeWebView.WebViewTransport transport)
            return false;

        transport.WebView = newWebView;
        resultMsg.SendToTarget();
        return true;
    }
}