using Android.OS;
using Android.Webkit;

namespace VpnHood.AppLib.Droid.Common.SpaWebView;

internal class AndroidSpaWebChromeClient : WebChromeClient
{
    public override bool OnCreateWindow(WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
    {
        if (view?.Context == null)
            return false;

        var newWebView = new WebView(view.Context);
        newWebView.SetWebViewClient(new AndroidSpaWebViewClient());
        if (resultMsg?.Obj is not WebView.WebViewTransport transport)
            return false;

        transport.WebView = newWebView;
        resultMsg.SendToTarget();
        return true;
    }
}