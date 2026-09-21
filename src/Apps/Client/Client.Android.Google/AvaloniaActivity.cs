using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Hosting.Avalonia.Droid;
using VpnHood.AppLib.Droid.Common.Activities;
using VpnHood.AppLib.Droid.Common.Constants;

namespace VpnHood.App.Client.Droid.Google;

// The Avalonia UI's activity, reached from MainActivity when the app asks for that UI
// (DebugCommands.AvaloniaUi) and from nowhere else: no launcher entry, not exported. AppCompat's
// theme because Avalonia's activity is an AppCompatActivity. Takes the access keys MainActivity
// takes, since it is handed MainActivity's intents.
[Activity(
    Label = AppConfigs.AppName,
    Theme = "@style/Theme.AppCompat.NoActionBar",
    Exported = false,
    LaunchMode = AndroidMainActivityConstants.LaunchMode,
    ScreenOrientation = AndroidMainActivityConstants.ScreenOrientation,
    ConfigurationChanges = AndroidMainActivityConstants.ConfigChanges)]
public class AvaloniaActivity : AndroidAppAvaloniaMainActivity<ClassicAvaloniaApp>
{
    protected override AndroidMainActivityOptions CreateActivityOptions()
    {
        return new AndroidMainActivityOptions {
            AccessKeySchemes = [MainActivity.AccessKeyScheme1, MainActivity.AccessKeyScheme2],
            AccessKeyMimes = [MainActivity.AccessKeyMime1, MainActivity.AccessKeyMime2, MainActivity.AccessKeyMime3]
        };
    }
}
