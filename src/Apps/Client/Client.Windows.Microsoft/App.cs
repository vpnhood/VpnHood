using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppLib.Stores.MicrosoftStore;
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

    // The product's options, and the Store's lines on top: its own update, offered once its automatic update
    // has had PromptDelay, and its rating dialog; usage data only after the person's own tick, in every
    // region (Store Policies 10.5.2). No release before it left a folder to import.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        var options = ClientAppOptions.Create(context, ClientAppConfigs.Load());
        options.IsAnonymousTrackerOptInRequired = true;
        options.UserReviewProvider = new MicrosoftStoreInAppUserReviewProvider();
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdaterProvider = new MicrosoftStoreAppUpdaterProvider()
        };
        return options;
    }
}
