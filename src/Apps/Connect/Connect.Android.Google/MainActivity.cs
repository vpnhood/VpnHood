using Android.Content;
using Android.Service.QuickSettings;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppUi.Hosting.Avalonia.Droid;
using VpnHood.AppUi.Presentation.Classic.Avalonia;

namespace VpnHood.App.Connect.Droid.Google;

// The launcher and the UI in one activity: Avalonia's. The theme above is the one that UI brings
// (Avalonia.Android, Resources/values/themes.xml). This head takes no access key, so it takes no
// intent that carries one.
[Activity(
    MainLauncher = true,
    Label = AppConfigs.AppName,
    Theme = "@style/Theme.VpnHood.Avalonia",
    LaunchMode = AndroidMainActivityConstants.LaunchMode,
    Exported = AndroidMainActivityConstants.Exported,
    WindowSoftInputMode = AndroidMainActivityConstants.WindowSoftInputMode,
    ScreenOrientation = AndroidMainActivityConstants.ScreenOrientation,
    ConfigurationChanges = AndroidMainActivityConstants.ConfigChanges)]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([TileService.ActionQsTilePreferences])]

// ReSharper disable once UnusedMember.Global
public class MainActivity : AndroidAvaloniaMainActivity<ClassicAvaloniaApp>;
