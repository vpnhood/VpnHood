

// In SDK-style projects such as this one, several assembly attributes that were historically
// defined in this file are now automatically added during build and populated with
// values defined in project properties. For details of which attributes are included
// and how to customise this process see: https://aka.ms/assembly-info-properties

[assembly: UsesFeature("android.software.leanback", Required = false)]
[assembly: UsesFeature("android.hardware.touchscreen", Required = false)]


namespace VpnHood.Client.App.Droid.Connect.Properties;
public static class AssemblyInfo
{
    public static Uri UpdateInfoUrl
    {
        get
        {
#if ANDROID_AAB
            return new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodConnect-android.json");
#else
            return new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodConnect-android-web.json");
#endif
        }
    }

    public static Uri StoreBaseUri
    {
        get
        {
#if DEBUG
            return new Uri("https://192.168.0.67:7077");
#else
            return new Uri("https://store-api.vpnhood.com");
#endif
        }
    } 
    public static string StoreAppId
    {
        get
        {
#if DEBUG
            return "3B5543E4-EBAD-4E73-A3CB-4CF26608BC29";
#else
            return "3B5543E4-EBAD-4E73-A3CB-4CF26608BC29"; // TODO must change
#endif
        }
    } 
    public static HttpClientHandler StoreHttpClientHandler
    {
        get
        {
#if DEBUG
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            return handler;
#else
            var handler = new HttpClientHandler();
            return handler // TODO Check
#endif
        }
    }
}