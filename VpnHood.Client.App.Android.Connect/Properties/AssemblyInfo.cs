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
    public static string GlobalServersAccessKey => GetMetadata("GlobalServersAccessKey", "vh://eyJ2Ijo0LCJuYW1lIjoiR2xvYmFsIFNlcnZlcnMgKEFkKSIsInNpZCI6IjEyNjUiLCJ0aWQiOiI3N2Q1ODYwMy1jZGNiLTRlZmMtOTkyZi1jMTMyYmUxZGUwZTMiLCJpYXQiOiIyMDI0LTA0LTI5VDIwOjAxOjIxLjY1OTgyODRaIiwic2VjIjoicExBbGYrVUhaVzJsTmhUQUJGT2x0QT09IiwiYWQiOnRydWUsInNlciI6eyJjdCI6IjIwMjQtMDQtMTVUMTk6NDQ6MzlaIiwiaG5hbWUiOiJtby5naXdvd3l2eS5uZXQiLCJocG9ydCI6MCwiaXN2IjpmYWxzZSwic2VjIjoidmFCcVU5UkMzUUhhVzR4RjVpYllGdz09IiwiY2giOiIzZ1hPSGU1ZWN1aUM5cStzYk83aGxMb2tRYkE9IiwidXJsIjoiaHR0cHM6Ly9yYXcuZ2l0aHVidXNlcmNvbnRlbnQuY29tL3Zwbmhvb2QvVnBuSG9vZC5GYXJtS2V5cy9tYWluL0ZyZWVfZW5jcnlwdGVkX3Rva2VuLnR4dCIsImVwIjpbIjUxLjgxLjIxMC4xNjQ6NDQzIiwiWzI2MDQ6MmRjMDoyMDI6MzAwOjo1Y2VdOjQ0MyJdfX0=");

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