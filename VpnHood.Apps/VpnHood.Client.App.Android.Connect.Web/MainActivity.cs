using Android.Content;
using Android.Service.QuickSettings;
using VpnHood.Client.App.Droid.Common.Activities;
using VpnHood.Client.App.Droid.Common.Constants;

namespace VpnHood.Client.App.Droid.Connect.Web;

[Activity(
    MainLauncher = true,
    Label = AndroidMainActivityConstants.Label,
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
    protected override AndroidAppMainActivityHandler CreateMainActivityHandler()
    {
        return new AndroidAppWebViewMainActivityHandler(this, new AndroidMainActivityWebViewOptions {
            SpaDefaultPort = App.SpaDefaultPort,
            SpaListenToAllIps = App.SpaListenToAllIps 
        });
    }
}