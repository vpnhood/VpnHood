using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppLib.Portal;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppUi.Hosting.Cli;
using VpnHood.AppUi.Hosting.Cli.Windows;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Connect.Windows.Web;

// The Windows head, which is the answers WindowsCliHost cannot give: the app to build when this
// process is the service, and the UI to show when it is the window. Everything else - which of the
// three this run is, the commands, the service - is the same on both Windows heads and lives in
// Hosting/Cli.
internal static class App
{
    [STAThread]
    private static int Main(string[] args)
    {
        return WindowsCliHost.Run(args, new CliHeadParams {
            AppId = AppConstants.AppId,
            AppOptionsFactory = CreateAppOptions,
            IsAddAccessKeySupported = ConnectAppOptions.IsAddAccessKeySupported,
            Ui = new AvaloniaDesktopHost<ClassicAvaloniaApp>()
        });
    }

    // The product's options, and this channel's lines on top: the installer from our site, whose
    // newer version the app announces with its download link.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        // the folder every release before the service used; the service's first start copies it in
        LegacyStorage.TryImport(context.StoragePath, "VpnHoodConnect");
        var appConfigs = ConnectAppConfigs.Load(typeof(App).Assembly);
        var options = ConnectAppOptions.Create(context, appConfigs);
        // Nothing forbids a typed code on this channel (App Review 3.1.1 binds the App Store head
        // only), and it is not shipped through a store, so an operator may point its buyers at its
        // own shop.
        options.Premium = ConnectAppOptions.CreatePremium(allowImportAccessCode: true, isPurchaseUrlSupported: true);
        options.AccountProvider = CreateAppAccountProvider(appConfigs, context);
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = appConfigs.GetUpdateInfoUrl(AppConstants.PackageTitle, "win-x64"),
            PromptDelay = TimeSpan.Zero
        };
        return options;
    }

    private static IAccountProvider? CreateAppAccountProvider(ConnectAppConfigs appConfigs, AppOptionsContext context)
    {
        try {
            // no Portal configured — ship without account features rather than half-wired ones
            if (appConfigs.PortalBaseUri == null)
                return null;

            // no external identity provider on this head: the portal's own password sign-in serves
            var portalAuthenticationProvider = new PortalAuthenticationProvider(context.StoragePath,
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
