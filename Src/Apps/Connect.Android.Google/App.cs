using System.Drawing;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.App.Client.Droid.Google.FirebaseUtils;
using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.Droid.Ads.VhAdMob;
using VpnHood.AppLib.Droid.Ads.VhChartboost;
using VpnHood.AppLib.Droid.Ads.VhInMobi;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Droid.GooglePlay;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Store;
using VpnHood.Core.Common.Logging;

namespace VpnHood.App.Client.Droid.Google;

[Application(
    Label = AppConfigs.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    AllowBackup = AndroidAppConstants.AllowBackup)]
[MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = AppConfigs.AdMobApplicationId)]
[MetaData("com.google.android.gms.ads.flag.OPTIMIZE_INITIALIZATION", Value = "true")]
[MetaData("com.google.android.gms.ads.flag.OPTIMIZE_AD_LOADING", Value = "true")]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    protected override AppOptions CreateAppOptions()
    {
        // lets init firebase analytics as single tone as soon as possible
        if (!FirebaseAnalyticsTracker.IsInit)
            FirebaseAnalyticsTracker.Init();

        // load app configs
        var appConfigs = AppConfigs.Load();

        // load app settings and resources
        var storageFolderPath = AppOptions.BuildStorageFolderPath("VpnHoodConnect");
        var resources = DefaultAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;
        resources.Colors.NavigationBarColor = Color.FromArgb(21, 14, 61);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(21, 14, 61);
        resources.Colors.ProgressBarColor = Color.FromArgb(231, 180, 129);

        return new AppOptions(appId: PackageName!, "VpnHoodConnect", AppConfigs.IsDebugMode) {
            StorageFolderPath = storageFolderPath,
            AccessKeys = [appConfigs.DefaultAccessKey],
            Resources = resources,
            UpdateInfoUrl = appConfigs.UpdateInfoUrl,
            UiName = "VpnHoodConnect",
            IsAddAccessKeySupported = false,
            UpdaterProvider = new GooglePlayAppUpdaterProvider(),
            AccountProvider = CreateAppAccountProvider(appConfigs, storageFolderPath),
            AdProviderItems = CreateAppAdProviderItems(appConfigs),
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            AdjustForSystemBars = false,
            TrackerFactory = new FirebaseAnalyticsTrackerFactory(),
            AdOptions = new AppAdOptions {
                PreloadAd = true
            }
        };
    }

    private static AppAdProviderItem[] CreateAppAdProviderItems(AppConfigs appConfigs)
    {
        var items = new List<AppAdProviderItem>();
        var initializeTimeout = TimeSpan.FromSeconds(5);

        items.Add(new AppAdProviderItem {
            AdProvider = AdMobInterstitialAdProvider.Create(appConfigs.AdMobInterstitialAdUnitId),
            ExcludeCountryCodes = ["CN"],
            ProviderName = "AdMob"
        });

        if (InMobiAdProvider.IsAndroidVersionSupported)
            items.Add(new AppAdProviderItem {
                AdProvider = InMobiAdProvider.Create(appConfigs.InmobiAccountId, appConfigs.InmobiPlacementId,
                    initializeTimeout, appConfigs.InmobiIsDebugMode),
                ProviderName = "InMobi"
            });

        if (ChartboostAdProvider.IsAndroidVersionSupported)
            items.Add(new AppAdProviderItem {
                AdProvider = ChartboostAdProvider.Create(appConfigs.ChartboostAppId, appConfigs.ChartboostAppSignature,
                    appConfigs.ChartboostAdLocation, initializeTimeout),
                ExcludeCountryCodes = ["IR", "CN"],
                ProviderName = "Chartboost"
            });

        items.Add(new AppAdProviderItem {
            AdProvider = AdMobInterstitialAdProvider.Create(appConfigs.AdMobInterstitialNoVideoAdUnitId),
            ExcludeCountryCodes = ["CN"],
            ProviderName = "AdMob-NoVideo"
        });

        items.Add(new AppAdProviderItem {
            AdProvider = AdMobRewardedAdProvider.Create(appConfigs.AdMobRewardedAdUnitId),
            ExcludeCountryCodes = ["CN"],
            ProviderName = "AdMob-Rewarded"
        });

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
            var googlePlayBillingProvider = new GooglePlayBillingProvider(authenticationProvider);
            var accountProvider = new StoreAccountProvider(authenticationProvider, googlePlayBillingProvider,
                appConfigs.StoreAppId);
            return accountProvider;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AppAccountService.");
            return null;
        }
    }
}