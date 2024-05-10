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
    public const string AdMonApplicationId = "ca-app-pub-8662231806304184~1740102860";
    public static Uri UpdateInfoUrl => new("https://github.com/vpnhood/VpnHood.Client.Connect/releases/latest/download/VpnHoodConnect-android.json");
    public static bool ListenToAllIps => IsDebugMode;
    public static int? DefaultSpaPort => IsDebugMode ? 9571 : 9570;
    public static string FirebaseClientId => "216585339900-pc0j9nlkl15gqbtp95da1j6gvttm8aol.apps.googleusercontent.com";
    public static Uri StoreBaseUri => new(GetMetadata("StoreBaseUri", "https://store-api.vpnhood.com"));
    public static Guid StoreAppId => Guid.Parse(GetMetadata("StoreAppId", "e7357285-775b-405e-aaca-096b1f95d3d0"));
    public static string RewardedAdUnitId => GetMetadata("RewardedAdUnitId", "ca-app-pub-8662231806304184/1656979755");
    public static string GlobalServersAccessKey => GetMetadata("GlobalServersAccessKey", "vh://eyJ2Ijo0LCJuYW1lIjoiR2xvYmFsIFNlcnZlcnMgKEFkKSIsInNpZCI6IjEyOTkiLCJ0aWQiOiI5YzkyNjE1Ni0yOGZhLTQ5NTctOTYxNi0zOGExN2U1MzQ0ZmYiLCJpYXQiOiIyMDI0LTA1LTEwVDA1OjI4OjM5LjA5MDQ3MjRaIiwic2VjIjoiK3NUZ1dBbDU3UEViZDZGOXpnQU9udz09IiwiYWQiOmZhbHNlLCJzZXIiOnsiY3QiOiIyMDI0LTA0LTE1VDE5OjQ0OjM5WiIsImhuYW1lIjoibW8uZ2l3b3d5dnkubmV0IiwiaHBvcnQiOjAsImlzdiI6ZmFsc2UsInNlYyI6InZhQnFVOVJDM1FIYVc0eEY1aWJZRnc9PSIsImNoIjoiM2dYT0hlNWVjdWlDOXErc2JPN2hsTG9rUWJBPSIsInVybCI6Imh0dHBzOi8vcmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbS92cG5ob29kL1Zwbkhvb2QuRmFybUtleXMvbWFpbi9GcmVlX2VuY3J5cHRlZF90b2tlbi50eHQiLCJlcCI6WyI1MS44MS4yMTAuMTY0OjQ0MyIsIlsyNjA0OjJkYzA6MjAyOjMwMDo6NWNlXTo0NDMiXX19");

    public static string GetMetadata(string key, string defaultValue) => VhUtil.GetAssemblyMetadata(typeof(AssemblyInfo).Assembly, key, defaultValue);
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