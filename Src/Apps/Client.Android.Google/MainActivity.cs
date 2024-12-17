using Android.Content;
using Android.Service.QuickSettings;
using VpnHood.AppLib.Droid.Common.Activities;
using VpnHood.AppLib.Droid.Common.Constants;

namespace VpnHood.App.Client.Droid.Google;

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
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataScheme = "content", DataMimeTypes = [AccessKeyMime1, AccessKeyMime2, AccessKeyMime3])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataSchemes = [AccessKeyScheme1, AccessKeyScheme2])]
public class MainActivity : AndroidAppMainActivity
{
    // https://android.googlesource.com/platform/libcore/+/android-5.0.2_r1/luni/src/main/java/libcore/net/MimeUtils.java
    private const string AccessKeyScheme1 = "vh";
    private const string AccessKeyScheme2 = "vhkey";
    private const string AccessKeyMime1 = "application/vhkey";
    private const string AccessKeyMime2 = "application/pgp-keys"; //.key
    private const string AccessKeyMime3 = "application/vnd.cinderella"; //.cdy

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