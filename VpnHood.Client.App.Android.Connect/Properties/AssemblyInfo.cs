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
    public static string GetMetadata(string key, string defaultValue) => VhUtil.GetAssemblyMetadata(typeof(AssemblyInfo).Assembly, key, defaultValue);
    public static Uri UpdateInfoUrl => new("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodConnect-android.json");
    public static bool ListenToAllIps => IsDebugMode;
    public static int? DefaultSpaPort => IsDebugMode ? 9571 : 9570;
    public static string FirebaseClientId => "216585339900-pc0j9nlkl15gqbtp95da1j6gvttm8aol.apps.googleusercontent.com";
    public static Uri StoreBaseUri => new(GetMetadata("StoreBaseUri", "https://store-api.vpnhood.com"));
    public static Guid StoreAppId => Guid.Parse(GetMetadata("StoreAppId", "e7357285-775b-405e-aaca-096b1f95d3d0"));
    public static string RewardedAdUnitId => GetMetadata("RewardedAdUnitId", "ca-app-pub-8662231806304184/1656979755");
    public static string GlobalServersAccessKey => GetMetadata("GlobalServersAccessKey", "vh://eyJ2Ijo0LCJuYW1lIjoiVnBuSG9vZCBHbG9iYWwgU2VydmVycyAoQWQpIiwic2lkIjoiMTI2NSIsInRpZCI6Ijc3ZDU4NjAzLWNkY2ItNGVmYy05OTJmLWMxMzJiZTFkZTBlMyIsInNlYyI6InBMQWxmK1VIWlcybE5oVEFCRk9sdEE9PSIsImFkIjp0cnVlLCJzZXIiOnsiY3QiOiIyMDI0LTA0LTE1VDE5OjQ0OjM5WiIsImhuYW1lIjoibW8uZ2l3b3d5dnkubmV0IiwiaHBvcnQiOjAsImlzdiI6ZmFsc2UsInNlYyI6InZhQnFVOVJDM1FIYVc0eEY1aWJZRnc9PSIsImNoIjoiM2dYT0hlNWVjdWlDOXErc2JPN2hsTG9rUWJBPSIsInVybCI6Imh0dHBzOi8vcmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbS92cG5ob29kL1Zwbkhvb2QuRmFybUtleXMvbWFpbi9GcmVlX2VuY3J5cHRlZF90b2tlbi50eHQiLCJlcCI6WyI1MS44MS4yMTAuMTY0OjQ0MyIsIlsyNjA0OjJkYzA6MjAyOjMwMDo6NWNlXTo0NDMiXX19");

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