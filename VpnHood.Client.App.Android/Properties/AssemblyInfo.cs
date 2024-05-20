// In SDK-style projects such as this one, several assembly attributes that were historically
// defined in this file are now automatically added during build and populated with
// values defined in project properties. For details of which attributes are included
// and how to customise this process see: https://aka.ms/assembly-info-properties

using VpnHood.Client.App.Abstractions;
#if GOOGLE_PLAY
using VpnHood.Client.App.Droid.GooglePlay;
#endif

[assembly: UsesFeature("android.software.leanback", Required = false)]
[assembly: UsesFeature("android.hardware.touchscreen", Required = false)]

namespace VpnHood.Client.App.Droid.Properties;
public static class AssemblyInfo
{
    public static bool ListenToAllIps => IsDebugMode;
    public static int? DefaultSpaPort => IsDebugMode ? 9581 : 9580;
    public static Uri UpdateInfoUrl
    {
        get
        {
#if GOOGLE_PLAY
            return new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android.json");
#else
            return new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android-web.json");
#endif
        }
    }

    // ReSharper disable once ReturnTypeCanBeNotNullable
    public static IAppUpdaterService? CreateUpdaterService()
    {
#if GOOGLE_PLAY
        // code clean up changes inline namespace to using
        return new GooglePlayAppUpdaterService();
#else
        return null;
#endif
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