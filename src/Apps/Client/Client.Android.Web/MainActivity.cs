using Android.Content;
using Android.Service.QuickSettings;
using VpnHood.AppLib.Droid.Common.Activities;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppUi.Hosting.Avalonia.Droid;
using VpnHood.AppUi.Presentation.Classic.Avalonia;

namespace VpnHood.App.Client.Droid.Web;

// The launcher and the UI in one activity: Avalonia's. The theme above is the one that UI brings
// (Avalonia.Android, Resources/values/themes.xml). The access keys a file or a link carries are
// this activity's too, so the intent filters that take them are here.
[Activity(
    MainLauncher = true,
    Label = AppConfigs.AppName,
    Theme = "@style/Theme.VpnHood.Avalonia",
    LaunchMode = AndroidMainActivityConstants.LaunchMode,
    Exported = AndroidMainActivityConstants.Exported,
    WindowSoftInputMode = AndroidMainActivityConstants.WindowSoftInputMode,
    ScreenOrientation = AndroidMainActivityConstants.ScreenOrientation,
    ConfigurationChanges = AndroidMainActivityConstants.ConfigChanges)]
[IntentFilter([TileService.ActionQsTilePreferences])]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataScheme = "content",
    DataMimeTypes = [AccessKeyMime1, AccessKeyMime2, AccessKeyMime3])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = [AccessKeyScheme1, AccessKeyScheme2])]
public class MainActivity : AndroidAvaloniaMainActivity<ClassicAvaloniaApp>
{
    // https://android.googlesource.com/platform/libcore/+/android-5.0.2_r1/luni/src/main/java/libcore/net/MimeUtils.java
    public const string AccessKeyScheme1 = "vh";
    public const string AccessKeyScheme2 = "vhkey";
    public const string AccessKeyMime1 = "application/vhkey";
    public const string AccessKeyMime2 = "application/pgp-keys"; //.key
    public const string AccessKeyMime3 = "application/vnd.cinderella"; //.cdy

    protected override AndroidMainActivityOptions CreateActivityOptions()
    {
        return new AndroidMainActivityOptions {
            AccessKeySchemes = [AccessKeyScheme1, AccessKeyScheme2],
            AccessKeyMimes = [AccessKeyMime1, AccessKeyMime2, AccessKeyMime3]
        };
    }
}
