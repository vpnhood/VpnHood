using Android.Content;
using Android.Service.QuickSettings;
using VpnHood.AppLib.Droid.Common.Activities;
using VpnHood.AppLib.Droid.Common.Constants;

namespace VpnHood.App.Client.Droid.Web;

[Activity(
    MainLauncher = true,
    Label = AppConfigs.AppName,
    Theme = AndroidMainActivityConstants.Theme,
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
        return new AndroidAppWebViewMainActivityHandler(this, new AndroidMainActivityWebViewOptions {
            SpaDefaultPort = AppConfigs.Instance.SpaDefaultPort,
            SpaListenToAllIps = AppConfigs.Instance.SpaListenToAllIps,
            AccessKeySchemes = [AccessKeyScheme1, AccessKeyScheme2],
            AccessKeyMimes = [AccessKeyMime1, AccessKeyMime2, AccessKeyMime3]
        });
    }
}