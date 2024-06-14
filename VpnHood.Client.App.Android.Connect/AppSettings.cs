using VpnHood.Common.Utils;
// ReSharper disable StringLiteralTypo

namespace VpnHood.Client.App.Droid.Connect;

internal class AppSettings : Singleton<AppSettings>
{
    // this is a test access key, you should replace it with your own access key. it is limited and can not be used in production
    public string DefaultAccessKey { get; init; } = "vh://eyJ2Ijo0LCJuYW1lIjoiVnBuSG9vZCBTYW1wbGUiLCJzaWQiOiIxMzAwIiwidGlkIjoiYTM0Mjk4ZDktY2YwYi00MGEwLWI5NmMtZGJhYjYzMWQ2MGVjIiwiaWF0IjoiMjAyNC0wNS0xMFQwNjo1MDozNC42ODQ4NjI4WiIsInNlYyI6Im9wcTJ6M0M0ak9rdHNodXl3c0VKNXc9PSIsImFkIjpmYWxzZSwic2VyIjp7ImN0IjoiMjAyNC0wNC0xNVQxOTo0NDozOVoiLCJobmFtZSI6Im1vLmdpd293eXZ5Lm5ldCIsImhwb3J0IjowLCJpc3YiOmZhbHNlLCJzZWMiOiJ2YUJxVTlSQzNRSGFXNHhGNWliWUZ3PT0iLCJjaCI6IjNnWE9IZTVlY3VpQzlxK3NiTzdobExva1FiQT0iLCJ1cmwiOiJodHRwczovL3Jhdy5naXRodWJ1c2VyY29udGVudC5jb20vdnBuaG9vZC9WcG5Ib29kLkZhcm1LZXlzL21haW4vRnJlZV9lbmNyeXB0ZWRfdG9rZW4udHh0IiwiZXAiOlsiNTEuODEuMjEwLjE2NDo0NDMiLCJbMjYwNDoyZGMwOjIwMjozMDA6OjVjZV06NDQzIl19fQ==";

    public Uri? UpdateInfoUrl { get; init; }
    public bool ListenToAllIps { get; init; } = IsDebugMode;
    public int? DefaultSpaPort { get; init; } = IsDebugMode ? 9571 : 9570;
    public string FirebaseClientId { get; init; } = "your_fire_base_id";

    // Store server property
    public Uri StoreBaseUri { get; init; } = new ("https://store-api.vpnhood.com");
    public Guid StoreAppId { get; init; } = Guid.Parse("00000000-0000-0000-0000-000000000000"); //your_vpnhood_store_app_id
    public bool StoreIgnoreSslVerification { get; init; }

    // AdMob (default value is AdMob test unit id), AdMobApplicationId must be set
    public const string AdMobApplicationId = "ca-app-pub-8662231806304184~1740102860"; //your_admob_app_id
    public string AdMobRewardedAdUnitId { get; init; } = "ca-app-pub-3940256099942544/5224354917";
    public string AdMobInterstitialAdUnitId { get; init; } = "ca-app-pub-3940256099942544/8691691433";
    public string AdMobInterstitialNoVideoAdUnitId { get; init; } = "AdMobInterstitialNoVideoAdUnitId";
    public string AdMobAppOpenAdUnitId { get; init; } = "ca-app-pub-3940256099942544/9257395921";
    
    // unity ad
    public string UnityAdGameId { get; init; } = "your_unity_appId";
    public string UnityAdInterstitialPlacementId { get; init; } = "your_unity_ads_interstitial_placement_id";

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