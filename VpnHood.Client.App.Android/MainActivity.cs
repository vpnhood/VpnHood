using Android.Content;
using Android.Content.PM;
using Android.Views;
using Android.Service.QuickSettings;
using VpnHood.Client.App.Abstractions;
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

[IntentFilter([TileService.ActionQsTilePreferences])]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataScheme = "content", DataMimeTypes = [AccessKeyMime1, AccessKeyMime2, AccessKeyMime3])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataSchemes = [AccessKeyScheme1, AccessKeyScheme2])]
public class MainActivity : AndroidAppWebViewMainActivity
{
    // https://android.googlesource.com/platform/libcore/+/android-5.0.2_r1/luni/src/main/java/libcore/net/MimeUtils.java
    public const string AccessKeyScheme1 = "vh";
    public const string AccessKeyScheme2 = "vhkey";
    public const string AccessKeyMime1 = "application/vhkey"; 
    public const string AccessKeyMime2 = "application/pgp-keys"; //.key
    public const string AccessKeyMime3 = "application/vnd.cinderella"; //.cdy

    public MainActivity()
    {
        AccessKeySchemes = [AccessKeyScheme1, AccessKeyScheme2];
        AccessKeyMimes = [AccessKeyMime1, AccessKeyMime2, AccessKeyMime3];
    }


    // ReSharper disable once ReturnTypeCanBeNotNullable
    protected override IAppUpdaterService? CreateAppUpdaterService()
    {
#if  GOOGLE_PLAY
        // ReSharper disable once RedundantNameQualifier
        return new VpnHood.Client.App.Droid.GooglePlay.GooglePlayAppUpdaterService(this);
#else
        return null;
#endif
    }
}
