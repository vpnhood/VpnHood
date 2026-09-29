using Android.OS;
using Android.Webkit;

using NativeWebView = Android.Webkit.WebView;

namespace VpnHood.AppUi.Hosting.WebView.Android;

// localToken: handed to the window's own client, for the loopback link it opens (AndroidWebViewClient).
internal class AndroidWebChromeClient(Func<string?> localToken) : WebChromeClient
{
    public override bool OnCreateWindow(NativeWebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
    {
        if (view?.Context == null)
            return false;

        var newWebView = new NativeWebView(view.Context);
        newWebView.SetWebViewClient(new AndroidWebViewClient(localToken));
        if (resultMsg?.Obj is not NativeWebView.WebViewTransport transport)
            return false;

        transport.WebView = newWebView;
        resultMsg.SendToTarget();
        return true;
    }
}