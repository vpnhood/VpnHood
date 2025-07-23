using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.App.Client.Droid.Google.FirebaseUtils;
using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Droid.Ads.VhAdMob;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Droid.GooglePlay;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Store;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.App.Client.Droid.Google;

[Application(
    Label = AppConfigs.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    Debuggable = IsDebugMode,
    AllowBackup = AndroidAppConstants.AllowBackup)]
[MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = AppConfigs.AdMobApplicationId)]
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
        var storageFolderPath = AppOptions.BuildStorageFolderPath("VpnHoodConnect");

        // load app settings and resources
        var resources = ConnectAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;

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
        // ReSharper disable once UseObjectOrCollectionInitializer
        var items = new List<AppAdProviderItem>();

        items.Add(new AppAdProviderItem {
            AdProvider = AdMobInterstitialAdProvider.Create(appConfigs.AdMobInterstitialAdUnitId),
            ExcludeCountryCodes = ["CN", "RU"],
            ProviderName = "AdMob"
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

        items.Add(new AppAdProviderItem {
            AdProvider = AdMobInterstitialAdProvider.Create(appConfigs.AdMobInterstitialNoVideoAdUnitId),
            ExcludeCountryCodes = ["CN", "RU"],
            ProviderName = "AdMob-NoVideo"
        });

        items.Add(new AppAdProviderItem {
            AdProvider = AdMobRewardedAdProvider.Create(appConfigs.AdMobRewardedAdUnitId),
            ExcludeCountryCodes = ["CN", "RU"],
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

#if DEBUG
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif
}