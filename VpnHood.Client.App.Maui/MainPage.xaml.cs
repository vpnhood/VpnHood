using Microsoft.Maui.Handlers;
using VpnHood.Client.App.UI;

namespace VpnHood.Client.App.Maui;

public partial class MainPage
{
    public MainPage()
    {
        InitializeComponent();
        MainWebView.Source = VpnHoodAppUi.Instance.Url.AbsoluteUri;
        MainWebView.Navigated += MainWebView_Navigated;
        MainWebView.Navigating += MainWebView_Navigating;

        //var z = (IWebViewHandler)MainWebView.Handler;
        //var a = ((IWebViewHandler)MainWebView.Handler).PlatformView;
        //a.SetWebChromeClient(new VpnHood.Client.App.Maui.MyWebChromeClient((IWebViewHandler)MainWebView.Handler));
    }


    private void MainWebView_Navigating(object? sender, WebNavigatingEventArgs e)
    {
        if (new Uri(e.Url).Host != VpnHoodAppUi.Instance.Url.Host)
        {
            e.Cancel = true;
            Launcher.OpenAsync("https://learn.microsoft.com/dotnet/maui");
        }
    }
    private void MainWebView_Navigated(object? sender, WebNavigatedEventArgs e)
    {
        _ = HideSplashScreen();
    }

    private async Task HideSplashScreen()
    {
        await SplashScreen.FadeTo(0, 2000);
        MainLayout.Remove(SplashScreen);
    }

    protected override bool OnBackButtonPressed()
    {

        if (!MainWebView.CanGoBack)
            return base.OnBackButtonPressed();

        MainWebView.GoBack();
        return true;
    }
}