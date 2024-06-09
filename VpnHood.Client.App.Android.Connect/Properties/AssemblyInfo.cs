// In SDK-style projects such as this one, several assembly attributes that were historically
// defined in this file are now automatically added during build and populated with
// values defined in project properties. For details of which attributes are included
// and how to customise this process see: https://aka.ms/assembly-info-properties

// ReSharper disable StringLiteralTypo

using VpnHood.Common.Utils;

[assembly: UsesFeature("android.software.leanback", Required = false)]
[assembly: UsesFeature("android.hardware.touchscreen", Required = false)]


namespace VpnHood.Client.App.Droid.Connect.Properties;
public static class AssemblyInfo
{
    public static Uri UpdateInfoUrl => new("https://github.com/vpnhood/VpnHood.Client.Connect/releases/latest/download/VpnHoodConnect-android.json");
    public static bool ListenToAllIps => IsDebugMode;
    public static int? DefaultSpaPort => IsDebugMode ? 9571 : 9570;
    public static string FirebaseClientId => "216585339900-pc0j9nlkl15gqbtp95da1j6gvttm8aol.apps.googleusercontent.com";
    
    // Store server property
    public static Uri StoreBaseUri => new(GetMetadata("StoreBaseUri", "https://store-api.vpnhood.com"));
    public static Guid StoreAppId => Guid.Parse(GetMetadata("StoreAppId", "e7357285-775b-405e-aaca-096b1f95d3d0"));
    
    // Ad network application id
    public const string AdMobApplicationId = "ca-app-pub-8662231806304184~1740102860";
    public const string UnityGameId = "5633286";
    
    // Ad unit ids
    // Default value is AdMob test unit id
    public static string AdMobRewardedAdUnitId => GetMetadata("RewardedAdUnitId", "ca-app-pub-3940256099942544/5224354917");
    public static string AdMobInterstitialAdUnitId => GetMetadata("InterstitialAdUnitId", "ca-app-pub-3940256099942544/1033173712");
    public static string AdMobInterstitialNoVideoAdUnitId => GetMetadata("InterstitialVideoAdUnitId", "ca-app-pub-3940256099942544/8691691433");
    
    public static string GlobalServersAccessKey => GetMetadata("GlobalServersAccessKey", "vh://eyJ2Ijo0LCJuYW1lIjoiVnBuSG9vZCBTYW1wbGUiLCJzaWQiOiIxMzAwIiwidGlkIjoiYTM0Mjk4ZDktY2YwYi00MGEwLWI5NmMtZGJhYjYzMWQ2MGVjIiwiaWF0IjoiMjAyNC0wNS0xMFQwNjo1MDozNC42ODQ4NjI4WiIsInNlYyI6Im9wcTJ6M0M0ak9rdHNodXl3c0VKNXc9PSIsImFkIjpmYWxzZSwic2VyIjp7ImN0IjoiMjAyNC0wNC0xNVQxOTo0NDozOVoiLCJobmFtZSI6Im1vLmdpd293eXZ5Lm5ldCIsImhwb3J0IjowLCJpc3YiOmZhbHNlLCJzZWMiOiJ2YUJxVTlSQzNRSGFXNHhGNWliWUZ3PT0iLCJjaCI6IjNnWE9IZTVlY3VpQzlxK3NiTzdobExva1FiQT0iLCJ1cmwiOiJodHRwczovL3Jhdy5naXRodWJ1c2VyY29udGVudC5jb20vdnBuaG9vZC9WcG5Ib29kLkZhcm1LZXlzL21haW4vRnJlZV9lbmNyeXB0ZWRfdG9rZW4udHh0IiwiZXAiOlsiNTEuODEuMjEwLjE2NDo0NDMiLCJbMjYwNDoyZGMwOjIwMjozMDA6OjVjZV06NDQzIl19fQ==");
    private static string GetMetadata(string key, string defaultValue) => VhUtil.GetAssemblyMetadata(typeof(AssemblyInfo).Assembly, key, defaultValue);
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