using Android.Content;
using Android.Service.QuickSettings;
using VpnHood.AppLib;
using VpnHood.AppLib.Droid.Common.Activities;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Droid.Common.SpaWebView;
using VpnHood.AppLib.Utils;

namespace VpnHood.App.Connect.Droid.Web;

[Activity(
    MainLauncher = true,
    Label = AppConfigs.AppName,
    Theme = AndroidMainActivityConstants.Theme,
    LaunchMode = AndroidMainActivityConstants.LaunchMode,
    Exported = AndroidMainActivityConstants.Exported,
    WindowSoftInputMode = AndroidMainActivityConstants.WindowSoftInputMode,
    ScreenOrientation = AndroidMainActivityConstants.ScreenOrientation,
    ConfigurationChanges = AndroidMainActivityConstants.ConfigChanges)]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([TileService.ActionQsTilePreferences])]
public class MainActivity : AndroidAppMainActivity
{
    // the Avalonia UI in place of the web view, when the debug command forces it: the launch goes
    // to that activity instead, because Avalonia's activity has a base class of its own
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (VpnHoodApp.Instance.HasDebugCommand(DebugCommands.AvaloniaUi))
            OnCreateRedirectingTo<AvaloniaActivity>(savedInstanceState);
        else
            base.OnCreate(savedInstanceState);
    }

    protected override AndroidAppMainActivityHandler CreateMainActivityHandler()
    {
        return new AndroidSpaWebViewMainActivityHandler(this, new AndroidSpaWebViewMainActivityOptions());
    }
}