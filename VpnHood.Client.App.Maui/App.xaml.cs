using Microsoft.Maui;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.UI;

namespace VpnHood.Client.App.Maui;

// ReSharper disable once RedundantExtendsListEntry
public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        MainPage = new MainPage();
        NavigationPage.SetHasNavigationBar(this, false);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);
        window.Width = UiDefaults.WindowSize.Width;
        window.Height = UiDefaults.WindowSize.Height;
        window.Title = UiResource.AppName;
        return window;
    }

    protected override void CleanUp()
    {
        base.CleanUp();
        if (VpnHoodAppUi.IsInit) VpnHoodAppUi.Instance.Dispose();
        if (VpnHoodApp.IsInit) _ = VpnHoodApp.Instance.DisposeAsync();
    }

    public new static App? Current => (App?)Application.Current;

    public Color? BackgroundColor =>
        (Resources.TryGetValue("Primary", out var primaryColor) == true)
            ? primaryColor as Color : null;


    private void UpdateIcon()
    {
        // update icon and text
        var icon = VpnHoodApp.Instance.State.ConnectionState switch
        {
            AppConnectionState.Connected => UiResource.VpnConnectedIcon,
            AppConnectionState.None => UiResource.VpnDisconnectedIcon,
            _ => UiResource.VpnConnectingIcon
        };

        var memStream = new MemoryStream(icon);
        //IconImageSource = ImageSource.FromStream(() => memStream);
    }
}
