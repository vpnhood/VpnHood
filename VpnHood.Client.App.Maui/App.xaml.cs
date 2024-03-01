using VpnHood.Client.App;
using VpnHood.Client.App.WebServer;

namespace VpnHood.Client.Samples.MauiAppSpaSample;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        MainPage = new MainPage();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);
        window.Width = VpnHoodApp.Instance.Resources.WindowSize.Width;
        window.Height = VpnHoodApp.Instance.Resources.WindowSize.Height;
        window.Title = VpnHoodApp.Instance.Resources.Strings.AppName;
        return window;
    }

    protected override void CleanUp()
    {
        base.CleanUp();
        if (VpnHoodAppWebServer.IsInit) VpnHoodAppWebServer.Instance.Dispose();
        if (VpnHoodApp.IsInit) _ = VpnHoodApp.Instance.DisposeAsync();
    }
}