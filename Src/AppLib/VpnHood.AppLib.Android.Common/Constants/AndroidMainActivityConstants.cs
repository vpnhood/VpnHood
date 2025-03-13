using Android.Content.PM;
using Android.Views;

namespace VpnHood.AppLib.Droid.Common.Constants;

public static class AndroidMainActivityConstants
{
    public const string? Label = "@string/app_name";

    public const string? Theme = "@android:style/Theme.DeviceDefault.NoActionBar";

    public const bool Exported = true;

    // resize app when keyboard is shown
    public const SoftInput WindowSoftInputMode = SoftInput.AdjustResize; 

    // required for TV
    public const ScreenOrientation ScreenOrientation = Android.Content.PM.ScreenOrientation.Unspecified; 

    // if SingleInstance, reopening the app after minimizing will not show the ad activity
    public const LaunchMode LaunchMode = Android.Content.PM.LaunchMode.Multiple; 

    public const ConfigChanges ConfigChanges =
        Android.Content.PM.ConfigChanges.Orientation | Android.Content.PM.ConfigChanges.ScreenSize |
        Android.Content.PM.ConfigChanges.LayoutDirection |
        Android.Content.PM.ConfigChanges.Keyboard | Android.Content.PM.ConfigChanges.KeyboardHidden |
        Android.Content.PM.ConfigChanges.FontScale |
        Android.Content.PM.ConfigChanges.Locale | Android.Content.PM.ConfigChanges.Navigation |
        Android.Content.PM.ConfigChanges.UiMode;
}