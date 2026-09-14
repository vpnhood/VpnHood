using Android.Content;
using VpnHood.AppLib.Droid.AvaloniaUI;
using VpnHood.AppLib.Droid.Common.Constants;

namespace VpnHood.App.Connect.Droid.Web;

// The Avalonia UI's activity beside the web view's, for the size measurement and the first runs
// of the one-bundle option (TV plan, Phase 4 packaging): a launcher of its own, so a device shows
// two icons and either UI can be opened - the runtime choice between them is not built. The Java
// name is fixed so a shell can start it (am start -n <package>/com.vpnhood.connect.android.web.AvaloniaActivity).
[Activity(
    Name = "com.vpnhood.connect.android.web.AvaloniaActivity",
    MainLauncher = true,
    Label = AppConfigs.AppName + " Avalonia",
    Theme = "@style/Theme.AppCompat.NoActionBar",
    Exported = true,
    LaunchMode = AndroidMainActivityConstants.LaunchMode,
    ScreenOrientation = AndroidMainActivityConstants.ScreenOrientation,
    ConfigurationChanges = AndroidMainActivityConstants.ConfigChanges)]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
public class AvaloniaActivity : AndroidAppAvaloniaMainActivity
{
}
