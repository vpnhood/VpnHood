

// In SDK-style projects such as this one, several assembly attributes that were historically
// defined in this file are now automatically added during build and populated with
// values defined in project properties. For details of which attributes are included
// and how to customise this process see: https://aka.ms/assembly-info-properties

[assembly: UsesFeature("android.software.leanback", Required = false)]
[assembly: UsesFeature("android.hardware.touchscreen", Required = false)]


namespace VpnHood.Client.App.Droid.Connect.Properties;
public static class AssemblyInfo
{
    public static Uri UpdateInfoUrl => new("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodConnect-android.json");
    public static Uri StoreBaseUri => IsDebugMode
        ? new Uri("https://192.168.0.67:7077")
        : new Uri("https://store-api.vpnhood.com");

    public static Guid StoreAppId => IsDebugMode
        ? Guid.Parse("3B5543E4-EBAD-4E73-A3CB-4CF26608BC29")
        : Guid.Parse("e7357285-775b-405e-aaca-096b1f95d3d0");

    public static bool ListenToAllIps => IsDebugMode;
    public static int? DefaultSpaPort => IsDebugMode ? 9571 : 9570;

    // ReSharper disable StringLiteralTypo
    public static string FirebaseClientId => "216585339900-pc0j9nlkl15gqbtp95da1j6gvttm8aol.apps.googleusercontent.com";
    public static string RewardedAdUnitId => "ca-app-pub-8662231806304184/1656979755";
    // ReSharper restore StringLiteralTypo

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