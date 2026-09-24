using Android.Content.PM;
using Android.Views;

namespace VpnHood.AppLib.App.Android.Constants;

public static class AndroidMainActivityConstants
{
    public const string? Label = "@string/app_name";

    // The platform's own, for an activity that asks for nothing else. The UI a head runs brings the
    // theme its activity needs, and a head names that one instead.
    public const string? Theme = "@android:style/Theme.DeviceDefault.NoActionBar";

    public const bool Exported = true;

    // resize app when keyboard is shown
    public const SoftInput WindowSoftInputMode = SoftInput.AdjustResize;

    // required for TV
    public const ScreenOrientation ScreenOrientation = global::Android.Content.PM.ScreenOrientation.Unspecified;

    // if SingleInstance, reopening the app after minimizing will not show the ad activity
    public const LaunchMode LaunchMode = global::Android.Content.PM.LaunchMode.Multiple;

    public const ConfigChanges ConfigChanges =
        global::Android.Content.PM.ConfigChanges.Orientation | global::Android.Content.PM.ConfigChanges.ScreenSize |
        global::Android.Content.PM.ConfigChanges.LayoutDirection |
        global::Android.Content.PM.ConfigChanges.Keyboard | global::Android.Content.PM.ConfigChanges.KeyboardHidden |
        global::Android.Content.PM.ConfigChanges.FontScale |
        global::Android.Content.PM.ConfigChanges.Locale | global::Android.Content.PM.ConfigChanges.Navigation |
        global::Android.Content.PM.ConfigChanges.UiMode;
}