using VpnHood.AppLib.App.Utils;
using Android.Content;
using Android.Runtime;
using Avalonia.Android;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib.App;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppUi.Hosting.Avalonia.Android;
using VpnHood.AppLib.App.Android;
using VpnHood.AppLib.App.Android.Constants;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.App.Connect.Android.Web;

[Application(
    Label = AppConfigs.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    AllowBackup = AndroidAppConstants.AllowBackup)]
[MetaData("CHANNEL", Value = "GitHub")]
// Avalonia's application base: this head's UI is Avalonia, and Avalonia 12 starts from the
// process's Application (its OnCreate, after the app below). MainActivity shows it.
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<ClassicAvaloniaApp>(javaReference, transfer)
{
    private AppOptions CreateAppOptions(AppConfigs appConfigs)
    {
        // load app settings and resources

        // The files this build's asset packages placed beside the app, read the way this
        // platform reads them: the IP-location database and the UI's store. The app extracts
        // what it must under its storage - the store once, for the in-process UI and for the
        // web host, which serves the same entries at /assets/ to a paired phone's page.
        var platformAssets = new AndroidAssetProvider(Application.Context);

        var appOptions = new AppOptions(appId: PackageName!, "VpnHoodConnect", AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            CustomData = appConfigs.CustomData,
            DeviceId = AndroidUtils.GetDeviceId(this), //this will be hashed using AppId
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            LogoAssetPath = appConfigs.LogoAssetPath,
            PrivacyConsentAssetName = appConfigs.PrivacyConsentAssetName,
            CompanyName = appConfigs.CompanyName,
            UiTheme = "violet",
            IsAddAccessKeySupported = false,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            WebUiPort = appConfigs.WebUiPort,
            AdjustForSystemBars = false,
            AllowRecommendUserReviewByServer = true,
            Premium = new AppPremiumOptions {
                Features = ConnectAppResources.PremiumFeatures,
                // nothing forbids a typed code on this channel (App Review 3.1.1 binds the App Store head only)
                AllowImportAccessCode = true,
                // not shipped through a store, so an operator may point its buyers at its own shop
                IsPurchaseUrlSupported = true
            },
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
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
            if (appConfigs.PortalBaseUri == null) {
                VhLogger.Instance.LogWarning("PortalBaseUri is not configured. Account features are disabled.");
                return null;
            }

            // No Google sign-in on this head, deliberately: an Android OAuth client is bound to a
            // package name AND signing certificate, and this sideloaded build shares neither with
            // the Play build. The portal's own password sign-in serves.
            var portalAuthenticationProvider = new PortalAuthenticationProvider(storageFolderPath,
                appConfigs.PortalBaseUri, appConfigs.AppId, [],
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);

            // the web-distribution store: plans priced by the portal, checkout in the browser
            var webBillingProvider = new PortalWebBillingProvider(appConfigs.PortalBaseUri, appConfigs.AppId,
                openUrl: (uiContext, url, _) => {
                    var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url.AbsoluteUri));
                    intent.AddFlags(ActivityFlags.NewTask);
                    ((AndroidUiContext) uiContext).Activity.StartActivity(intent);
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

    public override void OnCreate()
    {
        var appConfigs = AppConfigs.Load();

        // initialize the app flyer
        if (!string.IsNullOrEmpty(appConfigs.AppsFlyerDevKey))
            AppFlyerUtils.InitAppsFlyer(this, appConfigs.AppsFlyerDevKey, useRegionPolicy: !AppConfigs.IsDebugMode);

        // initialize the app
        VpnHoodAndroidApp.Init(() => CreateAppOptions(appConfigs));
        AndroidAvaloniaUi.Init();

        base.OnCreate();
    }

    public override void OnTerminate()
    {
        if (VpnHoodAndroidApp.IsInit)
            VpnHoodAndroidApp.Instance.Dispose();
    }
}