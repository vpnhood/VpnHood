using Android.Views;
using MauiApp3;
using Microsoft.Maui.Handlers;

namespace VpnHood.Client.App.Maui;

public class AndroidWebViewHandler : WebViewHandler
{
    protected override void ConnectHandler(Android.Webkit.WebView webView)
    {
        ConfigWebView(webView);
        base.ConnectHandler(webView);
    }


    private void ConfigWebView(Android.Webkit.WebView webView)
    {
        //webView.SetBackgroundColor(BackgroundColor);
        webView.Settings.JavaScriptEnabled = true;
        webView.Settings.DomStorageEnabled = true;
        webView.Settings.JavaScriptCanOpenWindowsAutomatically = true;
        webView.Settings.SetSupportMultipleWindows(true); 
        webView.SetLayerType(LayerType.Hardware, null);

        var webViewClient = new MyWebViewClient();
        webViewClient.PageLoaded += WebViewClient_PageLoaded;
        webView.SetWebViewClient(webViewClient);
        webView.SetWebChromeClient(new MyWebChromeClient(this));
        webView.Settings.JavaScriptEnabled = true;
    }

    private void WebViewClient_PageLoaded(object? sender, EventArgs e)
    {
        
    }
}