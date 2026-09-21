using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Hosting.Avalonia.Droid;
using VpnHood.AppLib.Droid.Common.Constants;

namespace VpnHood.App.Connect.Droid.Web;

// The Avalonia UI's activity, reached from MainActivity when the app asks for that UI
// (DebugCommands.AvaloniaUi) and from nowhere else: no launcher entry, not exported. AppCompat's
// theme because Avalonia's activity is an AppCompatActivity.
[Activity(
    Label = AppConfigs.AppName,
    Theme = "@style/Theme.AppCompat.NoActionBar",
    Exported = false,
    LaunchMode = AndroidMainActivityConstants.LaunchMode,
    ScreenOrientation = AndroidMainActivityConstants.ScreenOrientation,
    ConfigurationChanges = AndroidMainActivityConstants.ConfigChanges)]
public class AvaloniaActivity : AndroidAppAvaloniaMainActivity<ClassicAvaloniaApp>;
