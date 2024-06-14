using System.Drawing;
using Android.Runtime;
using Firebase.Crashlytics;
using Firebase;
using VpnHood.Client.App.Droid.Ads.VhAdMob;
using VpnHood.Client.App.Droid.Ads.VhUnityAds;
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
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    protected override AppOptions CreateAppOptions()
    {
        var appSettings = AppSettings.Create();
        InitFirebaseCrashlytics(appSettings);

        var storageFolderPath = AppOptions.DefaultStorageFolderPath;
        var googlePlayAuthenticationService = new GooglePlayAuthenticationService(appSettings.FirebaseClientId);
        var authenticationService = new StoreAuthenticationService(storageFolderPath, appSettings.StoreBaseUri, appSettings.StoreAppId, googlePlayAuthenticationService, appSettings.StoreIgnoreSslVerification);
        var googlePlayBillingService = new GooglePlayBillingService(authenticationService);
        var accountService = new StoreAccountService(authenticationService, googlePlayBillingService, appSettings.StoreAppId);

        var resources = DefaultAppResource.Resource;
        resources.Colors.NavigationBarColor = Color.FromArgb(100, 32, 25, 81);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(100, 32, 25, 81);

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
            AccountService = accountService,
            AdServices = [
                AdMobInterstitialAdService.Create(appSettings.AdMobInterstitialAdUnitId, true),
                AdMobInterstitialAdService.Create(appSettings.AdMobInterstitialNoVideoAdUnitId, false),
                UnityAdService.Create(appSettings.UnityAdGameId, appSettings.UnityAdInterstitialPlacementId, AppSettings.IsDebugMode)
            ],
            UiService = new AndroidAppUiService()
        };
    }

    private void InitFirebaseCrashlytics(AppSettings appSettings)
    {
        var firebaseOptions = new FirebaseOptions.Builder()
            .SetProjectId(appSettings.FirebaseProjectId)
            .SetApplicationId(appSettings.FirebaseApplicationId)
            .SetApiKey(appSettings.FirebaseApiKey)
            .Build();

        FirebaseApp.InitializeApp(this, firebaseOptions);
        FirebaseCrashlytics.Instance.SetCrashlyticsCollectionEnabled(true);
    }
}

