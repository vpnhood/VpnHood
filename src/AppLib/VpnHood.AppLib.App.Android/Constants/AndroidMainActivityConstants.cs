using Android.Content.PM;
using Android.Views;

namespace VpnHood.AppLib.App.Android.Constants;

/// <summary>Provides the values a head's main activity attribute takes: its label, theme, launch mode and the configuration changes it handles itself.</summary>
public static class AndroidMainActivityConstants
{
    /// <summary>The activity's label: the app's name.</summary>
    public const string? Label = "@string/app_name";

    /// <summary>The activity's theme: the platform's own, for an activity that asks for nothing else; a head whose UI needs another names that one instead.</summary>
    public const string? Theme = "@android:style/Theme.DeviceDefault.NoActionBar";

    /// <summary>Whether other apps may start the activity: yes, as the launcher does.</summary>
    public const bool Exported = true;

    /// <summary>The window's soft input mode: it resizes when the keyboard shows.</summary>
    public const SoftInput WindowSoftInputMode = SoftInput.AdjustResize;

    /// <summary>The screen's orientation: unspecified, as a TV needs; a UI that keeps a phone upright asks for it at run time.</summary>
    public const ScreenOrientation ScreenOrientation = global::Android.Content.PM.ScreenOrientation.Unspecified;

    /// <summary>
    /// The launch mode: single top, so an intent for the main activity while it is on top of its task goes to it rather
    /// than to a second one stacked over it.
    /// </summary>
    /// <remarks>
    /// Not single task, as reopening the app would clear the ad activity above it; nor single instance, which keeps the
    /// activity in a task of its own.
    /// </remarks>
    public const LaunchMode LaunchMode = global::Android.Content.PM.LaunchMode.SingleTop;

    /// <summary>
    /// The configuration changes the activity handles itself rather than being made again: orientation, screen size,
    /// layout direction, keyboard, font scale, locale, navigation and UI mode.
    /// </summary>
    public const ConfigChanges ConfigChanges =
        global::Android.Content.PM.ConfigChanges.Orientation | global::Android.Content.PM.ConfigChanges.ScreenSize |
        global::Android.Content.PM.ConfigChanges.LayoutDirection |
        global::Android.Content.PM.ConfigChanges.Keyboard | global::Android.Content.PM.ConfigChanges.KeyboardHidden |
        global::Android.Content.PM.ConfigChanges.FontScale |
        global::Android.Content.PM.ConfigChanges.Locale | global::Android.Content.PM.ConfigChanges.Navigation |
        global::Android.Content.PM.ConfigChanges.UiMode;
}