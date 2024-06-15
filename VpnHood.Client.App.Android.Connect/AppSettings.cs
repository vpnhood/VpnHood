using VpnHood.Common.Utils;
// ReSharper disable StringLiteralTypo

namespace VpnHood.Client.App.Droid.Connect;

internal class AppSettings : Singleton<AppSettings>
{
    public Uri? UpdateInfoUrl { get; init; }
    public bool ListenToAllIps { get; init; } = IsDebugMode;
    public int? DefaultSpaPort { get; init; } = IsDebugMode ? 9571 : 9570;
    
    // This is a test access key, you should replace it with your own access key.
    // It is limited and can not be used in production.
    public string DefaultAccessKey { get; init; } = "vh://eyJ2Ijo0LCJuYW1lIjoiVnBuSG9vZCBTYW1wbGUiLCJzaWQiOiIxMzAwIiwidGlkIjoiYTM0Mjk4ZDktY2YwYi00MGEwLWI5NmMtZGJhYjYzMWQ2MGVjIiwiaWF0IjoiMjAyNC0wNi0xNFQyMjozMjo1NS44OTQ5ODAyWiIsInNlYyI6Im9wcTJ6M0M0ak9rdHNodXl3c0VKNXc9PSIsInNlciI6eyJjdCI6IjIwMjQtMDYtMDVUMDQ6MTU6MzZaIiwiaG5hbWUiOiJtby5naXdvd3l2eS5uZXQiLCJocG9ydCI6MCwiaXN2IjpmYWxzZSwic2VjIjoidmFCcVU5UkMzUUhhVzR4RjVpYllGdz09IiwiY2giOiIzZ1hPSGU1ZWN1aUM5cStzYk83aGxMb2tRYkE9IiwidXJsIjoiaHR0cHM6Ly9yYXcuZ2l0aHVidXNlcmNvbnRlbnQuY29tL3Zwbmhvb2QvVnBuSG9vZC5GYXJtS2V5cy9tYWluL0ZyZWVfZW5jcnlwdGVkX3Rva2VuLnR4dCIsImVwIjpbIjUxLjgxLjIxMC4xNjQ6NDQzIiwiWzI2MDQ6MmRjMDoyMDI6MzAwOjo1Y2VdOjQ0MyJdLCJsb2MiOlsiVVMvT3JlZ29uIiwiVVMvVmlyZ2luaWEiXX19";
    
    // Google sign-in (It is created through Firebase)
    public string GoogleSignInClientId { get; init; } = "000000000000-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com"; //YOUR_FIREBASE_CLIENT_ID

    // Firebase Crashlytics
    public string? FirebaseProjectId { get; init; } //YOUR_FIREBASE_PROJECT_ID "client-xxxxx"
    public string? FirebaseApplicationId { get; init; } //YOUR_FIREBASE_APPLICATION_ID "0:000000000000:android:0000000000000000000000"
    public string? FirebaseApiKey { get; init; } //YOUR_FIREBASE_API_KEY "xxxxxxxxxxxxx_xxxxxxxxxxxxxxxxxxxxxxxxx"

    // VpnHood Store server
    public Uri StoreBaseUri { get; init; } = new ("https://store-api.vpnhood.com");
    public Guid StoreAppId { get; init; } = Guid.Parse("00000000-0000-0000-0000-000000000000"); //YOUR_VPNHOOD_STORE_APP_ID
    public bool StoreIgnoreSslVerification { get; init; }

    // AdMob
    // Default value is AdMob test AdUnit id, References: https://developers.google.com/admob/android/test-ads
    // NOTE: AdMobApplicationId MUST BE SET
    public const string AdMobApplicationId = "ca-app-pub-8662231806304184~1740102860"; //YOUR_ADMOB_APP_ID
    public string AdMobInterstitialAdUnitId { get; init; } = "ca-app-pub-3940256099942544/8691691433";
    public string AdMobInterstitialNoVideoAdUnitId { get; init; } = "ca-app-pub-3940256099942544/1033173712";
    public string AdMobRewardedAdUnitId { get; init; } = "ca-app-pub-3940256099942544/5224354917";
    public string AdMobAppOpenAdUnitId { get; init; } = "ca-app-pub-3940256099942544/9257395921";
    
    // UnityAd
    public string UnityAdGameId { get; init; } = "YOUR_UNITY_AD_UNIT_GAME_ID";
    public string UnityAdInterstitialPlacementId { get; init; } = "YOUR_UNITY_INTERSTITIAL_AD_UNIT_PLACEMENT_ID";

    public static AppSettings Create()
    {
        var appSettingsJson = VhUtil.GetAssemblyMetadata(typeof(AppSettings).Assembly, "AppSettings", "");
        return string.IsNullOrEmpty(appSettingsJson)
            ? new AppSettings()
            : VhUtil.JsonDeserialize<AppSettings>(appSettingsJson);
    }


    public static bool IsDebugMode
    {
        get
        {
#if DEBUG 
            return true;
#else
            return false;
#endif
        }
    }
}