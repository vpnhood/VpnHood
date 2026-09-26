using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib.App;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppUi.Hosting.Cli;
using VpnHood.AppUi.Hosting.Cli.Linux;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Connect.Linux.Web;

// The Connect head for Linux: the app to build when this process is the service, and the UI to
// show when it is the window. Everything else - the commands, the service, which of the three
// this run is - is the same on both Linux heads and lives in Hosting/Cli.
internal static class App
{
    // The product's options, and this channel's lines on top: the package from our site, which
    // updates itself, so the app names no update feed.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        var appConfigs = ConnectAppConfigs.Load(typeof(App).Assembly);
        var options = ConnectAppOptions.Create(context, appConfigs);
        // Nothing forbids a typed code on this channel (App Review 3.1.1 binds the App Store head
        // only), and it is not shipped through a store, so an operator may point its buyers at its
        // own shop.
        options.Premium = ConnectAppOptions.CreatePremium(allowImportAccessCode: true, isPurchaseUrlSupported: true);
        options.AccountProvider = CreateAppAccountProvider(appConfigs, context);
        options.UpdaterOptions = new AppUpdaterOptions {
            PromptDelay = TimeSpan.FromDays(1)
        };
        return options;
    }

    private static IAccountProvider? CreateAppAccountProvider(ConnectAppConfigs appConfigs, AppOptionsContext context)
    {
        try {
            // no Portal configured — ship without account features rather than half-wired ones
            if (appConfigs.PortalBaseUri == null) {
                VhLogger.Instance.LogWarning("PortalBaseUri is not configured. Account features are disabled.");
                return null;
            }

            // no external identity provider on this head: the portal's own password sign-in serves
            var portalAuthenticationProvider = new PortalAuthenticationProvider(context.StoragePath,
                appConfigs.PortalBaseUri, context.AppId, []);

            // the web-distribution store: plans priced by the portal, checkout in the browser of the
            // person at the window, which the window's own process opens
            var webBillingProvider = new PortalWebBillingProvider(appConfigs.PortalBaseUri, context.AppId);

            return new PortalAccountProvider(portalAuthenticationProvider, billingProvider: webBillingProvider,
                portalBaseUrl: appConfigs.PortalBaseUri, packageName: context.AppId);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AccountService.");
            return null;
        }
    }

    private static int Main(string[] args)
    {
        return LinuxCliHost.Run(args, new CliHeadParams {
            AppId = AppConstants.AppId,
            AppOptionsFactory = CreateAppOptions,
            // no profile to name: the profile commands and --profile are not offered
            IsAddAccessKeySupported = ConnectAppOptions.IsAddAccessKeySupported,
            Ui = new AvaloniaDesktopHost<ClassicAvaloniaApp>()
        });
    }
}
