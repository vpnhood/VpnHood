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
        window.Width = 400;
        window.Height = 700;
        return window;
    }
}
