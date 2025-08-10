using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.App.Client;
using VpnHood.App.Connect.Droid.Google.FirebaseUtils;
using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Droid.Ads.VhAdMob;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Droid.GooglePlay;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Store;
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
    : VpnHoodAndroidApp(javaReference, transfer)
{
    protected override AppOptions CreateAppOptions()
    {
        // lets init firebase analytics as single tone as soon as possible
        if (!FirebaseAnalyticsTracker.IsInit && !AppConfigs.IsDebug)
            FirebaseAnalyticsTracker.Init();

        // load app configs
        var appConfigs = AppConfigs.Load();
        var storageFolderPath = AppOptions.BuildStorageFolderPath("VpnHoodConnect");

        // load app settings and resources
        var resources = ConnectAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;

        return new AppOptions(appId: PackageName!, "VpnHoodConnect", AppConfigs.IsDebugMode) {
            CustomData = appConfigs.CustomData,
            StorageFolderPath = storageFolderPath,
            AccessKeys = [appConfigs.DefaultAccessKey],
            Resources = resources,
            UpdateInfoUrl = appConfigs.UpdateInfoUrl,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            UiName = "VpnHoodConnect",
            IsAddAccessKeySupported = false,
            UpdaterProvider = new GooglePlayAppUpdaterProvider(),
            UserReviewProvider = new GooglePlayInAppUserReviewProvider(AppConfigs.IsDebugMode),
            AccountProvider = CreateAppAccountProvider(appConfigs, storageFolderPath),
            AdProviderItems = CreateAppAdProviderItems(appConfigs),
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            AdjustForSystemBars = false,
            TrackerFactory = AppConfigs.IsDebug ? new NullTrackerFactory() : new FirebaseAnalyticsTrackerFactory(),
            PremiumFeatures = ConnectAppResources.PremiumFeatures,
            AllowRecommendUserReviewByServer = true,
            AdOptions = new AppAdOptions {
                PreloadAd = true
            },
        };
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

        return items.ToArray();
    }

    private static IAppAccountProvider? CreateAppAccountProvider(AppConfigs appConfigs, string storageFolderPath)
    {
        try {
            var authenticationExternalProvider = new GooglePlayAuthenticationProvider(appConfigs.GoogleSignInClientId);
            var authenticationProvider = new StoreAuthenticationProvider(storageFolderPath,
                new Uri(appConfigs.StoreBaseUri),
                appConfigs.StoreAppId, authenticationExternalProvider,
                ignoreSslVerification: appConfigs.StoreIgnoreSslVerification);
            var googlePlayBillingProvider = TryCreateBillingClient(authenticationProvider);
            var accountProvider = new StoreAccountProvider(authenticationProvider, googlePlayBillingProvider,
                appConfigs.StoreAppId);
            return accountProvider;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AppAccountService.");
            return null;
        }
    }

    private static IAppBillingProvider? TryCreateBillingClient(IAppAuthenticationProvider authenticationProvider)
    {
        try {
            return new GooglePlayBillingProvider(authenticationProvider);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create GooglePlayBillingProvider.");
            return null;
        }
    }
}