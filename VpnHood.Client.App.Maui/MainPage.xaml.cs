using VpnHood.Client.App;
using VpnHood.Client.App.WebServer;

namespace VpnHood.Client.Samples.MauiAppSpaSample;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        // Initialize SPA
        if (!VpnHoodAppWebServer.IsInit)
        {
            ArgumentNullException.ThrowIfNull(VpnHoodApp.Instance.Resources.SpaZipData);
            using var memoryStream = new MemoryStream(VpnHoodApp.Instance.Resources.SpaZipData);
            VpnHoodAppWebServer.Init(memoryStream);
        }

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