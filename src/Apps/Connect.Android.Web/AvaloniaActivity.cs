using VpnHood.AppLib.ClassicAvaloniaUi;
using VpnHood.AppLib.Droid.AvaloniaUI;
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
