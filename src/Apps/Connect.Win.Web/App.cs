using System.Windows;
using Microsoft.Extensions.Logging;
using VpnHood.App.Client;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppLib.Utils;
using VpnHood.AppLib.Win.Common;
using VpnHood.AppUi.Hosting.WebView.Windows;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.AppLib.Api.WebHost;

namespace VpnHood.App.Connect.Win.Web;

public class App : Application
{
    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();
        var resources = ConnectAppResources.Resources;
        var appOptions = new AppOptions(appId: appConfigs.AppId, "VpnHoodConnect", AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            // The listener a phone pairs with; without it IsRemoteAccessSupported is false.
            RemoteAccessHostProvider = () => VpnHoodAppWebServer.Instance,
            IpLocationZipData = ConnectAppResources.IpLocationZipData,
            UiName = "VpnHoodConnect",
            CustomData = appConfigs.CustomData,
            Resources = resources,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
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
            }
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
                    VpnHoodAppWin.OpenUrlInExternalBrowser(url);
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

    protected override void OnStartup(StartupEventArgs e)
    {
        // call base first to init app resources
        base.OnStartup(e);

        // the web UI, in this application's window
        VpnHoodAppWpf.Init();
    }

    [STAThread]
    public static void Main(string[] args)
    {
        // The app first, on its own; then the UI framework by the app's own setting: WPF hosts the
        // web UI, Avalonia the native one when the debug command forces it.
        try {
            VpnHoodAppWin.Init(CreateAppOptions, args);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not run the app.");
            return;
        }

        if (VpnHoodApp.Instance.HasDebugCommand(DebugCommands.AvaloniaUi))
            RunAvaloniaUi(args);
        else
            new App().Run();
    }

    // The Avalonia UI in its own window, opened from the tray as the web UI's is; Exit there ends
    // it, with the app.
    private static void RunAvaloniaUi(string[] args)
    {
        var appWin = VpnHoodAppWin.Instance;
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