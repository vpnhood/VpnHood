using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppUi.Hosting.Desktop;
using VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;
using VpnHood.AppUi.Presentation.Classic.Avalonia;

namespace VpnHood.App.Client.Windows.Microsoft;

// The Microsoft Store head, which is the answers WindowsVpnPluginDesktopHost cannot give: the app's
// options and the UI to show. Everything else - the app in the window's process, Windows' VPN host
// holding the tunnel - is the host's.
internal static class App
{
    [STAThread]
    private static int Main(string[] args)
    {
        return WindowsVpnPluginDesktopHost.Run(args, new DesktopInitParams {
            AppId = AppConstants.AppId,
            AppName = AppConstants.AppName,
            AppVersion = Version.Parse(AppConstants.AppVersion),
            AppOptionsFactory = CreateAppOptions,
            IsAddAccessKeySupported = ClientAppOptions.IsAddAccessKeySupported,
            Ui = new AvaloniaDesktopUi<ClassicAvaloniaApp>()
        });
    }

    // The product's options. The Store updates its apps, so no update feed, as on Linux; and no
    // release before it left a folder to import.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        var options = ClientAppOptions.Create(context, ClientAppConfigs.Load());
        options.UpdaterOptions = new AppUpdaterOptions();
        return options;
    }
}
