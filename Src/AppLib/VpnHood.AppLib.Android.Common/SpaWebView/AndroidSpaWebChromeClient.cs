using Android.OS;
using Android.Webkit;

namespace VpnHood.AppLib.Droid.Common;

internal class AndroidAppWebChromeClient : WebChromeClient
{
    public override bool OnCreateWindow(WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
    {
        if (view?.Context == null)
            return false;

        var newWebView = new WebView(view.Context);
        newWebView.SetWebViewClient(new AndroidAppWebViewClient());
        if (resultMsg?.Obj is not WebView.WebViewTransport transport)
            return false;

        transport.WebView = newWebView;
        resultMsg.SendToTarget();
        return true;
    }
}