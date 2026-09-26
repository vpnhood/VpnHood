using VpnHood.AppLib.App.Utils;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib.App;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Hosting.Avalonia.Android;
using VpnHood.AppLib.App.Android.Constants;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Connect.Android.Web;

[Application(
    Label = AppConstants.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    AllowBackup = AndroidAppConstants.AllowBackup)]
[MetaData("CHANNEL", Value = "GitHub")]
// The Avalonia UI's Application: it starts the app from the params below, then the UI, which
// MainActivity shows.
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AndroidAvaloniaApplication<ClassicAvaloniaApp>(javaReference, transfer)
{
    // read once, for AppsFlyer in every process and for the app in its own
    private readonly Lazy<ConnectAppConfigs> _appConfigs = new(() => ConnectAppConfigs.Load(typeof(App).Assembly));

    // Called by the platform only in the app's own process: never in the VPN service's or the tile's.
    protected override AppInitParams CreateInitParams()
    {
        var appConfigs = _appConfigs.Value;
        return new AppInitParams {
            AppId = PackageName ?? throw new InvalidOperationException("The app has no package name."),
            StorageFolderName = "VpnHoodConnect", // what every shipped build has used
            AppOptionsFactory = context => CreateAppOptions(context, appConfigs)
        };
    }

    // The product's options, and this channel's lines on top: the APK from GitHub.
    private static AppOptions CreateAppOptions(AppOptionsContext context, ConnectAppConfigs appConfigs)
    {
        var options = ConnectAppOptions.Create(context, appConfigs);
        // Nothing forbids a typed code on this channel (App Review 3.1.1 binds the App Store head
        // only), and it is not shipped through a store, so an operator may point its buyers at its
        // own shop.
        options.Premium = ConnectAppOptions.CreatePremium(allowImportAccessCode: true, isPurchaseUrlSupported: true);
        options.AccountProvider = CreateAppAccountProvider(appConfigs, context);
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = appConfigs.GetUpdateInfoUrl(AppConstants.PackageTitle, "android-web"),
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

            // No Google sign-in on this head, deliberately: an Android OAuth client is bound to a
            // package name AND signing certificate, and this sideloaded build shares neither with
            // the Play build. The portal's own password sign-in serves.
            var portalAuthenticationProvider = new PortalAuthenticationProvider(context.StoragePath,
                appConfigs.PortalBaseUri, context.AppId, []);

            // the web-distribution store: plans priced by the portal, checkout in the browser
            var webBillingProvider = new PortalWebBillingProvider(appConfigs.PortalBaseUri, context.AppId);

            return new PortalAccountProvider(portalAuthenticationProvider, billingProvider: webBillingProvider,
                portalBaseUrl: appConfigs.PortalBaseUri, packageName: context.AppId);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AccountService.");
            return null;
        }
    }

    public override void OnCreate()
    {
        // initialize the app flyer
        var appConfigs = _appConfigs.Value;
        if (!string.IsNullOrEmpty(appConfigs.AppsFlyerDevKey))
            AppFlyerUtils.InitAppsFlyer(this, appConfigs.AppsFlyerDevKey, useRegionPolicy: !AppConstants.IsDebugMode);

        // the app, then the UI
        base.OnCreate();
    }
}