using Android.OS;

namespace VpnHood.Client.Device.Droid;

// todo remove in .net android
public static class OperatingSystem
{
    public static bool IsAndroidVersionAtLeast(int major)
    {
        return (int)Build.VERSION.SdkInt >= major;
    }
}