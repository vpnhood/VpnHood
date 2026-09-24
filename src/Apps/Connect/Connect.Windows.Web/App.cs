using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib.App;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppLib.App.Utils;
using VpnHood.AppLib.App.Windows;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.App.Connect.Windows.Web;

public static class App
{
    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();

        // The files this build's asset packages placed beside the app, read the way this
        // platform reads them: the IP-location database and the UI's store. The app extracts
        // what it must under its storage - the store once, for the in-process UI and for the
        // web host, which serves the same entries at /assets/ to a paired phone's page.
        var platformAssets = new FolderAssetProvider(AppContext.BaseDirectory);

        var appOptions = new AppOptions(appId: appConfigs.AppId, "VpnHoodConnect", AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            UiTheme = "violet",
            CustomData = appConfigs.CustomData,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            LogoAssetPath = appConfigs.LogoAssetPath,
            PrivacyConsentAssetName = appConfigs.PrivacyConsentAssetName,
            CompanyName = appConfigs.CompanyName,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = false,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            WebUiPort = appConfigs.WebUiPort,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            AllowRecommendUserReviewByServer = true,
            LogServiceOptions = {
                SingleLineConsole = false
            },
            Premium = new AppPremiumOptions {
                Features = ConnectAppResources.PremiumFeatures,
                // nothing forbids a typed code on this channel (App Review 3.1.1 binds the App Store head only)
                AllowImportAccessCode = true,
                // not shipped through a store, so an operator may point its buyers at its own shop
                IsPurchaseUrlSupported = true
            },
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                UpdaterProvider = new AdvancedInstallerUpdaterProvider(),
                PromptDelay = TimeSpan.FromDays(1)
            },
            IpLocationZipAsset = new Asset(platformAssets, "iplocations/IpLocations.zip"),
            UiZipAssets = [new Asset(platformAssets, "assets/ui.zip")],
            // the page a paired phone opens: this same UI, as its browser build
            WebRootZipAsset = new Asset(platformAssets, "assets/web-root.zip"),
            WebHostFactory = new VpnHoodAppWebHostFactory()
        };

        appOptions.AccountProvider = CreateAppAccountProvider(appConfigs, appOptions.StorageFolderPath);
        return appOptions;
    }

    private static IAccountProvider? CreateAppAccountProvider(AppConfigs appConfigs, string storageFolderPath)
    {
        try {
            // no Portal configured — ship without account features rather than half-wired ones
            if (appConfigs.PortalBaseUri == null)
                return null;

            // no external identity provider on this head: the portal's own password sign-in serves
            var portalAuthenticationProvider = new PortalAuthenticationProvider(storageFolderPath,
                appConfigs.PortalBaseUri, appConfigs.AppId, [],
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);

            // the web-distribution store: plans priced by the portal, checkout in the browser
            var webBillingProvider = new PortalWebBillingProvider(appConfigs.PortalBaseUri, appConfigs.AppId,
                openUrl: (_, url, _) => {
                    VpnHoodWindowsApp.OpenUrlInExternalBrowser(url);
                    return Task.CompletedTask;
                },
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);

            return new PortalAccountProvider(portalAuthenticationProvider, billingProvider: webBillingProvider,
                portalBaseUrl: appConfigs.PortalBaseUri, packageName: appConfigs.AppId,
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AccountService.");
            return null;
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        // The app first, on its own; then its UI, which is Avalonia in this process. A window is
        // all this head starts: the web host is the paired device's, and the pairing screen is
        // what puts it up.
        try {
            VpnHoodWindowsApp.Init(CreateAppOptions, args);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not run the app.");
            return;
        }

        RunAvaloniaUi(args);
    }

    // The Avalonia UI in its own window, opened from the tray; Exit there ends it, with the app.
    private static void RunAvaloniaUi(string[] args)
    {
        var appWin = VpnHoodWindowsApp.Instance;
        appWin.OpenMainWindowRequested += (_, _) => AvaloniaDesktopHost.ShowMainWindow();
        appWin.ExitRequested += (_, _) => AvaloniaDesktopHost.Shutdown();
        try {
            AvaloniaDesktopHost.Run<ClassicAvaloniaApp>(args, appWin.ShowWindowAfterStart);
        }
        finally {
            appWin.Dispose();
        }
    }
}