using System.Drawing;
using Android.Runtime;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Droid.Ads.VhAdMob;
using VpnHood.Client.App.Droid.Ads.VhInMobi;
using VpnHood.Client.App.Droid.Ads.VhChartboost;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.GooglePlay;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.Store;
using VpnHood.Client.Device.Droid.Utils;
using VpnHood.Common.Logging;

namespace VpnHood.Client.App.Droid.Connect;

[Application(
    Label = "@string/app_name",
    Icon = "@mipmap/appicon",
    Banner = "@mipmap/banner", // for TV
    NetworkSecurityConfig = "@xml/network_security_config", // required for localhost
    SupportsRtl = true, AllowBackup = true)]
[MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = AppSettings.AdMobApplicationId)]
[MetaData("com.google.android.gms.ads.flag.OPTIMIZE_INITIALIZATION", Value = "true")]
[MetaData("com.google.android.gms.ads.flag.OPTIMIZE_AD_LOADING", Value = "true")]

public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    private FirebaseAnalytics? _analytics;

    protected override AppOptions CreateAppOptions()
    {
        // initialize Firebase services
        try {
            _analytics = FirebaseAnalytics.GetInstance(this);
        }
        catch {
            /* ignored*/
        }

        try {
            FirebaseCrashlytics.Instance.SetCrashlyticsCollectionEnabled(Java.Lang.Boolean.True);
        }
        catch {
            /* ignored */
        }

        // load app settings and resources
        var storageFolderPath = AppOptions.DefaultStorageFolderPath;
        var appSettings = AppSettings.Create();
        var resources = DefaultAppResource.Resource;
        resources.Colors.NavigationBarColor = Color.FromArgb(21, 14, 61);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(21, 14, 61);
        resources.Colors.ProgressBarColor = Color.FromArgb(231, 180, 129);

        return new AppOptions {
            AppId = PackageName!,
            DeviceId = AndroidUtil.GetDeviceId(this), //this will be hashed using AppId
            StorageFolderPath = storageFolderPath,
            AccessKeys = [appSettings.DefaultAccessKey],
            Resource = DefaultAppResource.Resource,
            UpdateInfoUrl = appSettings.UpdateInfoUrl,
            UiName = "VpnHoodConnect",
            IsAddAccessKeySupported = false,
            UpdaterProvider = new GooglePlayAppUpdaterProvider(),
            CultureProvider = AndroidAppCultureProvider.CreateIfSupported(),
            AccountProvider = CreateAppAccountProvider(appSettings, storageFolderPath),
            AdProviderItems = CreateAppAdProviderItems(appSettings),
            AllowEndPointTracker = appSettings.AllowEndPointTracker,
            Tracker = _analytics != null ? new AnalyticsTracker(_analytics) : null,
            UiProvider = new AndroidUiProvider(),
            LogAnonymous = !AppSettings.IsDebugMode,
            AdOptions = new AppAdOptions {
                PreloadAd = true
            }
        };
    }

    // Set the clientId as userId to the analytics
    public override void OnCreate()
    {
        base.OnCreate();
        _analytics?.SetUserId(VpnHoodApp.Instance.Features.ClientId);
    }

    private static AppAdProviderItem[] CreateAppAdProviderItems(AppSettings appSettings)
    {
        return [
            new AppAdProviderItem {
                AdProvider = AdMobInterstitialAdProvider.Create(appSettings.AdMobInterstitialAdUnitId),
                ExcludeCountryCodes = ["IR", "CN"],
                ProviderName = "AdMob",
            },

            new AppAdProviderItem {
                AdProvider = InMobiAdProvider.Create(appSettings.InmobiAccountId, appSettings.InmobiPlacementId, appSettings.InmobiIsDebugMode),
                ProviderName = "InMobi",
            },

            new AppAdProviderItem {
                AdProvider = ChartboostAdProvider.Create(appSettings.ChartboostAppId, appSettings.ChartboostAppSignature, appSettings.ChartboostAdLocation),
                ExcludeCountryCodes = ["IR", "CN"],
                ProviderName = "Chartboost",
            },

            new AppAdProviderItem {
                AdProvider = AdMobInterstitialAdProvider.Create(appSettings.AdMobInterstitialNoVideoAdUnitId),
                ExcludeCountryCodes = ["CN"],
                ProviderName = "AdMob-NoVideo",
            },
        ];
    }

    private static StoreAccountProvider? CreateAppAccountProvider(AppSettings appSettings, string storageFolderPath)
    {
        try {
            var authenticationExternalProvider = new GooglePlayAuthenticationProvider(appSettings.GoogleSignInClientId);
            var authenticationProvider = new StoreAuthenticationProvider(storageFolderPath, appSettings.StoreBaseUri,
                appSettings.StoreAppId, authenticationExternalProvider, appSettings.StoreIgnoreSslVerification);
            var googlePlayBillingProvider = new GooglePlayBillingProvider(authenticationProvider);
            var accountProvider = new StoreAccountProvider(authenticationProvider, googlePlayBillingProvider, appSettings.StoreAppId);
            return accountProvider;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AppAccountService.");
            return null;
        }
    }
}
