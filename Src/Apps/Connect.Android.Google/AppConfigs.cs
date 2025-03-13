using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
// ReSharper disable HeuristicUnreachableCode
namespace VpnHood.App.Client.Droid.Google;

internal class AppConfigs : AppConfigsBase<AppConfigs>
{
    public const string AppName = IsDebugMode ? "VpnHOOD! CONNECT (DEBUG)" : "VpnHood! CONNECT";

    public string? UpdateInfoUrl { get; set; } =
        "https://github.com/vpnhood/VpnHood.App.Connect/releases/latest/download/VpnHoodConnect-Android.json";

    public int? SpaDefaultPort { get; set; } = IsDebugMode ? 9571 : 9570;
    public bool SpaListenToAllIps { get; set; } = IsDebugMode;
    public bool AllowEndPointTracker { get; set; }

    // This is a test access key, you should replace it with your own access key.
    // It is limited and can not be used in production.
    public string DefaultAccessKey { get; set; } = ClientOptions.SampleAccessKey;

    // Google sign-in (It is created through Firebase)
    public string GoogleSignInClientId { get; set; } =
        "000000000000-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com"; //YOUR_FIREBASE_CLIENT_ID

    // VpnHood Store server
    public string StoreBaseUri { get; set; } = new("https://store-api.vpnhood.com");

    public Guid StoreAppId { get; set; } =
        Guid.Parse("00000000-0000-0000-0000-000000000000"); //YOUR_VPNHOOD_STORE_APP_ID

    public bool StoreIgnoreSslVerification { get; set; } = IsDebugMode;

    // AdMob
    // Default value is AdMob test AdUnit id, References: https://developers.google.com/admob/android/test-ads
    // NOTE: AdMobApplicationId MUST BE SET
    public const string AdMobApplicationId = "ca-app-pub-8662231806304184~1740102860"; //YOUR_ADMOB_APP_ID
    public string AdMobInterstitialAdUnitId { get; set; } = "ca-app-pub-3940256099942544/8691691433";
    public string AdMobInterstitialNoVideoAdUnitId { get; set; } = "ca-app-pub-3940256099942544/1033173712";
    public string AdMobRewardedAdUnitId { get; set; } = "ca-app-pub-3940256099942544/5224354917";

    // Chartboost
    public string ChartboostAppId { get; set; } = "000000000000000000000000"; //YOUR_CHATBOOST_APP_ID

    public string ChartboostAppSignature { get; set; } =
        "0000000000000000000000000000000000000000"; //YOUR_CHATBOOST_APP_SIGNATURE

    public string ChartboostAdLocation { get; set; } = "YOUR_CHARTBOOST_AD_LOCATION";

    // Inmobi
    public string InmobiAccountId { get; set; } = "000000000000000000000000"; //YOUR_INMMOBI_ACCOUNT_ID
    public string InmobiPlacementId { get; set; } = "000000000000"; //YOUR_INMOBI_PLACEMENT_ID
    public bool InmobiIsDebugMode { get; set; } = IsDebugMode;

    public static AppConfigs Load()
    {
        var appConfigs = new AppConfigs();
        appConfigs.Merge("AppSettings");
        appConfigs.Merge("AppSettings_Environment");
        return appConfigs;
    }

#if DEBUG
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif
}