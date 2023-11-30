using Android.Content;
using Android.Content.PM;
using Android.Views;
using Android.Service.QuickSettings;
using VpnHood.Client.App.Droid.Common.Activities;

namespace VpnHood.Client.App.Droid;

[Activity(Label = "@string/app_name",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    MainLauncher = true,
    Exported = true,
    WindowSoftInputMode = SoftInput.AdjustResize, // resize app when keyboard is shown
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.Unspecified,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
                           ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]

[IntentFilter(new[] { TileService.ActionQsTilePreferences })]
[IntentFilter(new[] { Intent.ActionMain }, Categories = new[] { Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher })]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault }, DataScheme = "content", DataMimeTypes = new[] { AccessKeyMime1, AccessKeyMime2, AccessKeyMime3 })]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataSchemes = new[] { AccessKeyScheme1, AccessKeyScheme2 })]
public class MainActivity : AndroidAppWebViewMainActivity
{
    public const string AccessKeyScheme1 = "vh";
    public const string AccessKeyScheme2 = "vhkey";
    public const string AccessKeyMime1 = "application/vhkey";
    public const string AccessKeyMime2 = "application/key";
    public const string AccessKeyMime3 = "application/vnd.cinderella";

    public MainActivity()
    {
        AccessKeySchemes = new[] { AccessKeyScheme1, AccessKeyScheme2 };
        AccessKeyMimes = new[] { AccessKeyMime1, AccessKeyMime2, AccessKeyMime3 };
    }
}
