using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;

namespace VpnHood.Client.App.Maui;

public partial class AppWebView
{
    protected override void OnHandlerChanged()
    {
        if (Handler?.PlatformView is not WebView2 webView)
            return;

        ConfigWebView(webView);
        base.OnHandlerChanged();
    }


    private void ConfigWebView(WebView2 webView)
    {
        if (Handler == null)
            throw new Exception("WebViewHandler is not initialized.");

        //// covert to ThemePrimaryColor to Android Color
        //if (App.Current?.BackgroundColor != null)
        //    webView.SetBackgroundColor(Android.Graphics.Color.ParseColor(App.Current.BackgroundColor.ToHex()));

        //webView.Settings.JavaScriptEnabled = true;
        //webView.Settings.DomStorageEnabled = true;
        //webView.Settings.JavaScriptCanOpenWindowsAutomatically = true;
        //webView.Settings.SetSupportMultipleWindows(true);

        //var webViewClient = new MyWebViewClient((WebViewHandler)Handler);
        //webView.SetWebViewClient(webViewClient);
        //webView.SetWebChromeClient(new MyWebChromeClient((WebViewHandler)Handler));
        //webView.Settings.JavaScriptEnabled = true;
    }
}