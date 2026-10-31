using Android.Content;
using Android.Service.QuickSettings;
using VpnHood.AppLib.App.Android.Activities;
using VpnHood.AppLib.App.Android.Constants;
using VpnHood.AppUi.Hosting.Avalonia.Android;
using VpnHood.AppUi.Presentation.Classic.Avalonia;

namespace VpnHood.App.Client.Android.Web;

// The launcher and the UI in one activity: Avalonia's. The theme above is the one that UI brings
// (Avalonia.Android, Resources/values/themes.xml). The access keys a file or a link carries are
// this activity's too, so the intent filters that take them are here, in the forms CLIENT takes
// (ClientAccessKeys).
[Activity(
    // Launchers keep the app's home-screen icon by this Java name. The generated default hashes the
    // namespace and the assembly name, and the publish build's assembly is <project>.csproj.tmp, so it
    // is pinned to the name the app shipped under.
    Name = "crc642cb32472de6f7e9e.MainActivity",
    MainLauncher = true,
    Label = AppConstants.AppName,
    Theme = "@style/Theme.VpnHood.Avalonia",
    LaunchMode = AndroidMainActivityConstants.LaunchMode,
    Exported = AndroidMainActivityConstants.Exported,
    WindowSoftInputMode = AndroidMainActivityConstants.WindowSoftInputMode,
    ScreenOrientation = AndroidMainActivityConstants.ScreenOrientation,
    ConfigurationChanges = AndroidMainActivityConstants.ConfigChanges)]
[IntentFilter([TileService.ActionQsTilePreferences])]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataScheme = "content",
    DataMimeTypes = [ClientAccessKeys.Mime1, ClientAccessKeys.Mime2, ClientAccessKeys.Mime3])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = [ClientAccessKeys.Scheme1, ClientAccessKeys.Scheme2])]
public class MainActivity : AndroidAvaloniaMainActivity<ClassicAvaloniaApp>
{
    protected override AndroidMainActivityOptions CreateActivityOptions()
    {
        return new AndroidMainActivityOptions {
            AccessKeySchemes = [ClientAccessKeys.Scheme1, ClientAccessKeys.Scheme2],
            AccessKeyMimes = [ClientAccessKeys.Mime1, ClientAccessKeys.Mime2, ClientAccessKeys.Mime3]
        };
    }
}
