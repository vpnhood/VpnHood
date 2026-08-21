using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.App.Client;
using VpnHood.App.Connect.Droid.Google.FirebaseUtils;
using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Droid.Ads.VhAdMob;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Droid.GooglePlay;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Toolkit.Logging;

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
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : Application(javaReference, transfer)
{
    private static AppOptions CreateAppOptions()
    {
        // load app configs
        var appConfigs = AppConfigs.Load();
        var storageFolderPath = AppOptions.BuildStorageFolderPath("VpnHoodConnect");

        // load app settings and resources
        var resources = ConnectAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;

        return new AppOptions(appId: appConfigs.AppId, "VpnHoodConnect", AppConfigs.IsDebugMode) {
            CustomData = appConfigs.CustomData,
            StorageFolderPath = storageFolderPath,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            Resources = resources,
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
        // lets init firebase analytics as single tone as soon as possible
        if (!FirebaseAnalyticsTracker.IsInit && !AppConfigs.IsDebug)
            FirebaseAnalyticsTracker.Init();

        // init app
        VpnHoodAndroidApp.Init(CreateAppOptions);
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