using Android.OS;

namespace VpnHood.Client.App.Droid;

internal static  class OperatingSystem
{
    public static bool IsAndroidVersionAtLeast(int major)
    {
        return (int)Build.VERSION.SdkInt >= major;
    }
}