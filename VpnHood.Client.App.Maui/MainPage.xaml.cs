using VpnHood.Client.App.WebServer;

namespace VpnHood.Client.App.Maui;

public partial class MainPage
{
    public MainPage()
    {
        InitializeComponent();
        MainWebView.Source = VpnHoodAppWebServer.Instance.Url.AbsoluteUri;
        MainWebView.Navigated += MainWebView_Navigated;
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