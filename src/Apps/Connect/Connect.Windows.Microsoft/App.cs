using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.Stores.MicrosoftStore;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppUi.Hosting.Desktop;
using VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Connect.Windows.Microsoft;

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
            IsAddAccessKeySupported = ConnectAppOptions.IsAddAccessKeySupported,
            Ui = new AvaloniaDesktopUi<ClassicAvaloniaApp>()
        });
    }

    // The product's options, and the Store's lines on top: its own update, offered once its automatic update
    // has had PromptDelay, and its rating dialog; usage data only after the person's own tick, in every
    // region (Store Policies 10.5.2). Microsoft lets a non-game app on PCs sell through its own shop (10.8.1
    // and 10.8.6), so the website edition's: a typed code and the portal's checkout in the browser.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        var appConfigs = ConnectAppConfigs.Load(typeof(App).Assembly);
        var options = ConnectAppOptions.Create(context, appConfigs);
        options.IsAnonymousTrackerOptInRequired = true;
        options.UserReviewProvider = new MicrosoftStoreInAppUserReviewProvider();
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdaterProvider = new MicrosoftStoreAppUpdaterProvider()
        };
        options.Premium = ConnectAppOptions.CreatePremium(allowImportAccessCode: true, isPurchaseUrlSupported: true);
        options.AccountProvider = CreateAppAccountProvider(appConfigs, context);
        return options;
    }

    private static IAccountProvider? CreateAppAccountProvider(ConnectAppConfigs appConfigs, AppOptionsContext context)
    {
        try {
            // no Portal configured — ship without account features rather than half-wired ones
            if (appConfigs.PortalBaseUri == null)
                return null;

            // no external identity provider on this head: the portal's own password sign-in serves
            var portalAuthenticationProvider = new PortalAuthenticationProvider(context.NoBackupStoragePath,
                appConfigs.PortalBaseUri, context.AppId, []);

            // the web-distribution store: plans priced by the portal, checkout in the browser the
            // window opens as the person at it
            var webBillingProvider = new PortalWebBillingProvider(appConfigs.PortalBaseUri, context.AppId);

            return new PortalAccountProvider(portalAuthenticationProvider, billingProvider: webBillingProvider,
                portalBaseUrl: appConfigs.PortalBaseUri, packageName: context.AppId);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AccountService.");
            return null;
        }
    }
}
