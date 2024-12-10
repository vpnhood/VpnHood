// In SDK-style projects such as this one, several assembly attributes that were historically
// defined in this file are now automatically added during build and populated with
// values defined in project properties. For details of which attributes are included
// and how to customise this process see: https://aka.ms/assembly-info-properties

[assembly: UsesFeature("android.software.leanback", Required = false)]
[assembly: UsesFeature("android.hardware.touchscreen", Required = false)]

namespace VpnHood.Client.App.Droid.Properties;

public static class AssemblyInfo
{
    public static bool IsDebugMode {
        get {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}