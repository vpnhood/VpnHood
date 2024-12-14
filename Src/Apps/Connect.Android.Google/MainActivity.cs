using Android.Content;
using Android.Service.QuickSettings;
using VpnHood.AppLibs.Droid.Common.Activities;
using VpnHood.AppLibs.Droid.Common.Constants;

namespace VpnHood.Apps.Client.Droid.Google;

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

// ReSharper disable once UnusedMember.Global
public class MainActivity : AndroidAppMainActivity
{
    protected override AndroidAppMainActivityHandler CreateMainActivityHandler()
    {
        return new AndroidAppWebViewMainActivityHandler(this, new AndroidMainActivityWebViewOptions {
            SpaDefaultPort = AppConfigs.Instance.SpaDefaultPort,
            SpaListenToAllIps = AppConfigs.Instance.SpaListenToAllIps // if true it will cause crash in network change
        });
    }
}