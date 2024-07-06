using Android.Content;
using Android.Content.PM;
using Android.Service.QuickSettings;
using Android.Views;
using VpnHood.Client.App.Droid.Common.Activities;
using VpnHood.Client.App.Droid.Properties;

namespace VpnHood.Client.App.Droid;

[Activity(
    Label = "@string/app_name",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    MainLauncher = true,
    Exported = true,
    WindowSoftInputMode = SoftInput.AdjustResize, // resize app when keyboard is shown
    // LaunchMode = LaunchMode.SingleInstance, if set; then open the app after minimize will not show ad activity
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
                           ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]

[IntentFilter([TileService.ActionQsTilePreferences])]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataScheme = "content", DataMimeTypes = [AccessKeyMime1, AccessKeyMime2, AccessKeyMime3])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataSchemes = [AccessKeyScheme1, AccessKeyScheme2])]
public class MainActivity : AndroidAppMainActivity
{
    // https://android.googlesource.com/platform/libcore/+/android-5.0.2_r1/luni/src/main/java/libcore/net/MimeUtils.java
    public const string AccessKeyScheme1 = "vh";
    public const string AccessKeyScheme2 = "vhkey";
    public const string AccessKeyMime1 = "application/vhkey";
    public const string AccessKeyMime2 = "application/pgp-keys"; //.key
    public const string AccessKeyMime3 = "application/vnd.cinderella"; //.cdy

    protected override AndroidAppMainActivityHandler CreateMainActivityHandler()
    {
        return new AndroidAppWebViewMainActivityHandler(this, new AndroidMainActivityWebViewOptions
        {
            DefaultSpaPort = AssemblyInfo.DefaultSpaPort,
            ListenToAllIps = AssemblyInfo.ListenToAllIps,
            AccessKeySchemes = [AccessKeyScheme1, AccessKeyScheme2],
            AccessKeyMimes = [AccessKeyMime1, AccessKeyMime2, AccessKeyMime3]
        });
    }
}
