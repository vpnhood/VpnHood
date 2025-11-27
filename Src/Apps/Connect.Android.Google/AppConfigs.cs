using System.Text.Json;
using VpnHood.App.Client;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
// ReSharper disable HeuristicUnreachableCode
namespace VpnHood.App.Connect.Droid.Google;

internal class AppConfigs : AppConfigsBase<AppConfigs>, IRequiredAppConfigs
{
    public const string AppName = IsDebugMode ? "VpnHOOD! CONNECT (DEBUG)" : "VpnHood! CONNECT";
    public string AppId { get; set; } = Application.Context.PackageName!;

    public Uri? UpdateInfoUrl { get; set; } =
        new("https://github.com/vpnhood/VpnHood.App.Connect/releases/latest/download/VpnHoodConnect-Android.json");

    public int? WebUiPort { get; set; } = IsDebugMode ? 7701 : 7770;
    public string? DefaultAccessKey { get; set; } = IsDebugMode ? ClientOptions.SampleAccessKey : null;
    public string? Ga4MeasurementId { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }
    public bool AllowEndPointTracker { get; set; }
    public JsonElement? CustomData { get; set; }

    // This is a test access key, you should replace it with your own access key.
    // It is limited and can not be used in production.

    // Google sign-in (It is created through Firebase)
    public string GoogleSignInClientId { get; set; } =
        "000000000000-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com"; //YOUR_FIREBASE_CLIENT_ID

    // VpnHood Store server
    public string StoreBaseUri { get; set; } = new("https://store-api.vpnhood.com");

    public Guid StoreAppId { get; set; } =
        Guid.Parse("00000000-0000-0000-0000-000000000000"); //YOUR_VPNHOOD_STORE_APP_ID

    public string[] GooglePlayProductIds { get; set; } = [
        "vpnhood_1_month_subscription", 
        "vpnhood_1_year_subscription"
    ];

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

    public string[]? AllowedPrivateDnsProviders { get; set; } = [
        "one.one.one.one",
        "family.cloudflare-dns.com",
        "adult-filter-dns.cleanbrowsing.org",
        "family-filter-dns.cleanbrowsing.org",
        "family.dot.dns.yandex.net",
        "dns.google",
        "dns.quad9.net",
        "common.dot.dns.yandex.net",
        "unfiltered.adguard-dns.com",
        "dot.sb",
        "dns.sb",
        "anycast.uncensoreddns.org",
        "unicast.uncensoreddns.org",
        "dot.libredns.gr"
    ];

    public static AppConfigs Load()
    {
        var appConfigs = new AppConfigs();
        appConfigs.Merge("AppSettings");
        appConfigs.Merge("AppSettings_Environment");
        return appConfigs;
    }

    // make dynamic to prevent warning of unreachable code in Release mode
    public static bool IsDebug => IsDebugMode;

#if DEBUG
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif
}