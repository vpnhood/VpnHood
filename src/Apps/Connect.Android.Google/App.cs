using VpnHood.AppLib.Utils;
using Android.Runtime;
using Avalonia.Android;
using Microsoft.Extensions.Logging;
using VpnHood.App.Client;
using VpnHood.App.Connect.Droid.Google.FirebaseUtils;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppUi.Hosting.Avalonia.Droid;
using VpnHood.AppLib.Droid.Ads.VhAdMob;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Droid.GooglePlay;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.App.Connect.Droid.Google;

[Application(
    Label = AppConfigs.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    Debuggable = AppConfigs.IsDebugMode,
    AllowBackup = AndroidAppConstants.AllowBackup)]
[MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = AppConfigs.AdMobApplicationId)]
// Avalonia's application base: the Avalonia UI ships beside the web view, and Avalonia 12 starts
// from the process's Application (its OnCreate, after the app below). AvaloniaActivity shows it,
// when MainActivity hands the launch over (DebugCommands.AvaloniaUi).
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<ClassicAvaloniaApp>(javaReference, transfer)
{
    private static AppOptions CreateAppOptions()
    {
        // load app configs
        var appConfigs = AppConfigs.Load();
        var storageFolderPath = AppOptions.BuildStorageFolderPath("VpnHoodConnect");

        // load app settings and resources
        var resources = ConnectAppResources.Resources;

        return new AppOptions(appId: appConfigs.AppId, "VpnHoodConnect", AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            // what this head serves: the SPA, or the Avalonia UI's browser build for a paired phone
            WebHostFactory = new VpnHoodAppWebHostFactory(new WebHostOptions { WebRootZip = ConnectAppResources.WebRootZip }),
            IpLocationZipAsset = new Asset(new AndroidAssetProvider(Application.Context), "iplocations/IpLocations.zip"),
            CustomData = appConfigs.CustomData,
            StorageFolderPath = storageFolderPath,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            Resources = resources,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            // The store already took this acceptance at install - see AppOptions.
            IsLicenseAgreementRequired = false,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            UiName = "VpnHoodConnect",
            IsAddAccessKeySupported = false,
            UserReviewProvider = new GooglePlayInAppUserReviewProvider(),
            AccountProvider = CreateAppAccountProvider(appConfigs, storageFolderPath),
            AdProviderItems = CreateAppAdProviderItems(appConfigs),
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            AdjustForSystemBars = false,
            TrackerFactory = AppConfigs.IsDebug ? new NullTrackerFactory() : new FirebaseAnalyticsTrackerFactory(),
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            WebUiPort = appConfigs.WebUiPort,
            AllowRecommendUserReviewByServer = true,
            Premium = new AppPremiumOptions {
                Features = ConnectAppResources.PremiumFeatures,
                // nothing forbids a typed code on this channel (App Review 3.1.1 binds the App Store head only)
                AllowImportAccessCode = true
                // IsPurchaseUrlSupported stays false: Play forbids steering a buyer to an outside shop,
                // so no operator token may raise a web-purchase link in this build
            },
            AdOptions = new AppAdOptions {
                PreloadAd = true,
                RejectAdBlocker = true,
                AllowedPrivateDnsProviders = appConfigs.AllowedPrivateDnsProviders
            },
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                UpdaterProvider = new GooglePlayAppUpdaterProvider()
            }
        };
    }

    public override void OnCreate()
    {
        // Init Firebase Analytics as a singleton as soon as possible, but not in the tile process: it
        // never reports anything, and FirebaseInitProvider runs only in the default process, so this
        // call would be the whole Firebase start-up cost inside the tile's executing-service window
        // (Play ANR group "Executing service …QuickLaunchTileService"). The VPN service process keeps
        // it on purpose: this same call is what gives Crashlytics crash reporting there.
        if (!FirebaseAnalyticsTracker.IsInit && !AppConfigs.IsDebug && !QuickLaunchTileService.IsTileProcess)
            FirebaseAnalyticsTracker.Init();

        // init app
        VpnHoodAndroidApp.Init(CreateAppOptions);
        AndroidAvaloniaUi.Init();
        base.OnCreate();
    }

    public override void OnTerminate()
    {
        if (VpnHoodAndroidApp.IsInit)
            VpnHoodAndroidApp.Instance.Dispose();
    }

    private static AppAdProviderItem[] CreateAppAdProviderItems(AppConfigs appConfigs)
    {
        // ReSharper disable once UseObjectOrCollectionInitializer
        var items = new List<AppAdProviderItem>();

        // interstitial
        items.Add(new AppAdProviderItem {
            AdProvider = AdMobInterstitialAdProvider.Create(appConfigs.AdMobInterstitialAdUnitId),
            ExcludeCountryCodes = ["CN", "RU"],
            ProviderName = "AdMob"
        });

        // rewarded ad
        items.Add(new AppAdProviderItem {
            AdProvider = AdMobRewardedAdProvider.Create(appConfigs.AdMobRewardedAdUnitId),
            ExcludeCountryCodes = ["CN", "RU"],
            ProviderName = "AdMob-Rewarded"
        });

        items.Add(new AppAdProviderItem {
            AdProvider = new InternalInAdProvider(),
            ProviderName = "InternalAd",
            IsFallback = true
        });


        /*var initializeTimeout = TimeSpan.FromSeconds(5);
        if (InMobiAdProvider.IsAndroidVersionSupported)
            items.Add(new AppAdProviderItem {
                AdProvider = InMobiAdProvider.Create(
                    appConfigs.InmobiAccountId, appConfigs.InmobiPlacementId, initializeTimeout, appConfigs.InmobiIsDebugMode),
                ExcludeCountryCodes = ["CN", "RU"],
                ProviderName = "InMobi"
            });*/

        //if (ChartboostAdProvider.IsAndroidVersionSupported)
        //    items.Add(new AppAdProviderItem {
        //        AdProvider = ChartboostAdProvider.Create(appConfigs.ChartboostAppId, appConfigs.ChartboostAppSignature,
        //            appConfigs.ChartboostAdLocation, initializeTimeout),
        //        ExcludeCountryCodes = ["IR", "CN"],
        //        ProviderName = "Chartboost"
        //    });

        return [.. items];
    }

    private static IAccountProvider? CreateAppAccountProvider(AppConfigs appConfigs, string storageFolderPath)
    {
        try {
            // no Portal configured — ship without account features rather than half-wired ones
            if (appConfigs.PortalBaseUri == null) {
                VhLogger.Instance.LogWarning("PortalBaseUri is not configured. Account features are disabled.");
                return null;
            }

            var authenticationExternalProvider = new GooglePlayAuthenticationProvider(appConfigs.GoogleSignInClientId);
            var googlePlayBillingProvider = TryCreateBillingClient();

            var portalAuthenticationProvider = new PortalAuthenticationProvider(storageFolderPath,
                appConfigs.PortalBaseUri, appConfigs.AppId, [authenticationExternalProvider],
                restoreCredentialProvider: new GoogleRestoreCredentialProvider(),
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);

            // The portal owns the catalog: it maps each store product to the plan that redeems it, so a
            // product it does not map cannot become an entitlement — and cannot be sold here either.
            return new PortalAccountProvider(portalAuthenticationProvider, googlePlayBillingProvider,
                portalBaseUrl: appConfigs.PortalBaseUri, packageName: appConfigs.AppId,
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AccountService.");
            return null;
        }
    }

    private static IBillingProvider? TryCreateBillingClient()
    {
        try {
            return new GooglePlayBillingProvider();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create GooglePlayBillingProvider.");
            return null;
        }
    }
}