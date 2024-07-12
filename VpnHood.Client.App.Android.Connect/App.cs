using System.Drawing;
using Android.Runtime;
using Firebase.Crashlytics;
using Firebase.Analytics;
using VpnHood.Client.App.Droid.Ads.VhAdMob;
using VpnHood.Client.App.Droid.Ads.VhChartboost;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.GooglePlay;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.Store;

namespace VpnHood.Client.App.Droid.Connect;

[Application(
    Label = "@string/app_name",
    Icon = "@mipmap/appicon",
    Banner = "@mipmap/banner", // for TV
    NetworkSecurityConfig = "@xml/network_security_config",  // required for localhost
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
        try { _analytics = FirebaseAnalytics.GetInstance(this); } catch { /* ignored*/ }
        try { FirebaseCrashlytics.Instance.SetCrashlyticsCollectionEnabled(true); } catch { /* ignored */ }

        // load app settings and resources
        var storageFolderPath = AppOptions.DefaultStorageFolderPath;
        var appSettings = AppSettings.Create();
        var resources = DefaultAppResource.Resource;
        resources.Colors.NavigationBarColor = Color.FromArgb(21, 14, 61);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(21, 14, 61);
        resources.Colors.ProgressBarColor = Color.FromArgb(231, 180, 129);
        
        return new AppOptions
        {
            StorageFolderPath = storageFolderPath,
            AccessKeys = [appSettings.DefaultAccessKey],
            Resource = DefaultAppResource.Resource,
            UpdateInfoUrl = appSettings.UpdateInfoUrl,
            UiName = "VpnHoodConnect",
            IsAddAccessKeySupported = false,
            UpdaterService = new GooglePlayAppUpdaterService(),
            CultureService = AndroidAppAppCultureService.CreateIfSupported(),
            AccountService = CreateAppAccountService(appSettings, storageFolderPath),
            AllowEndPointTracker = appSettings.AllowEndPointTracker,
            Tracker = _analytics != null ? new AnalyticsTracker(_analytics) : null,
            AdServices = [
                AdMobInterstitialAdService.Create(appSettings.AdMobInterstitialAdUnitId, true),
                AdMobInterstitialAdService.Create(appSettings.AdMobInterstitialNoVideoAdUnitId, false),
                ChartboostService.Create(appSettings.ChartboostAppId, appSettings.ChartboostAppSignature, appSettings.ChartboostAdLocation)
            ],
            UiService = new AndroidAppUiService(),
            LogAnonymous = !AppSettings.IsDebugMode,
            AdOptions = new AppAdOptions
            {
                PreloadAd = true
            }
        };
    }

    // Set the clientId as userId to the analytics
    public override void OnCreate()
    {
        base.OnCreate();
        _analytics?.SetUserId(VpnHoodApp.Instance.Settings.ClientId.ToString());
    }

    private static StoreAccountService? CreateAppAccountService(AppSettings appSettings, string storageFolderPath)
    {
        try
        {
            var googlePlayAuthenticationService = new GooglePlayAuthenticationService(appSettings.GoogleSignInClientId);
            var authenticationService = new StoreAuthenticationService(storageFolderPath, appSettings.StoreBaseUri, appSettings.StoreAppId, googlePlayAuthenticationService, appSettings.StoreIgnoreSslVerification);
            var googlePlayBillingService = new GooglePlayBillingService(authenticationService);
            var accountService = new StoreAccountService(authenticationService, googlePlayBillingService, appSettings.StoreAppId);
            return accountService;
        }
        catch (Exception)
        {
            // ignored
            return null;
        }
    }
}