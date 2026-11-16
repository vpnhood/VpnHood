using Android.Content;
using Android.Service.QuickSettings;
using VpnHood.AppLogic.App.Android.Constants;
using VpnHood.AppHosting.Avalonia.Android;
using VpnHood.AppUi.Avalonia.Classic;

namespace VpnHood.App.Connect.Android.Web;

// The launcher and the UI in one activity: Avalonia's. The theme above is the one that UI brings
// (Avalonia.Android, Resources/values/themes.xml). This head takes no access key, so it takes no
// intent that carries one.
[Activity(
    // Launchers keep the app's home-screen icon by this Java name. The generated default hashes the
    // namespace and the assembly name, and the publish build's assembly is <project>.csproj.tmp, so it
    // is pinned to the name the app shipped under.
    Name = "crc64f76f66fcd9cdddf1.MainActivity",
    MainLauncher = true,
    Label = AppConstants.AppName,
    Theme = "@style/Theme.VpnHood.Avalonia",
    LaunchMode = AndroidMainActivityConstants.LaunchMode,
    Exported = AndroidMainActivityConstants.Exported,
    WindowSoftInputMode = AndroidMainActivityConstants.WindowSoftInputMode,
    ScreenOrientation = AndroidMainActivityConstants.ScreenOrientation,
    ConfigurationChanges = AndroidMainActivityConstants.ConfigChanges)]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([TileService.ActionQsTilePreferences])]
public class MainActivity : AndroidAvaloniaMainActivity<ClassicAvaloniaApp>;
