using Android.Runtime;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Hosting.Avalonia.Android;
using VpnHood.AppLib.App.Android.Constants;

namespace VpnHood.App.Client.Android.Samsung;

[Application(
    Label = AppConstants.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    AllowBackup = AndroidAppConstants.AllowBackup)]
// The Avalonia UI's Application: it starts the app from the params below, then the UI, which
// MainActivity shows.
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AndroidAvaloniaApplication<ClassicAvaloniaApp>(javaReference, transfer)
{
    // Called by the platform only in the app's own process: never in the VPN service's or the tile's.
    protected override AppInitParams CreateInitParams()
    {
        return new AppInitParams {
            AppId = PackageName ?? throw new InvalidOperationException("The app has no package name."),
            AppName = AppConstants.AppName,
            AppVersion = Version.Parse(AppConstants.AppVersion),
            StorageFolderName = "VpnHood", // what every shipped build has used
            AppOptionsFactory = CreateAppOptions
        };
    }

    // The product's options as they are. The licence agreement keeps its default, on (AppOptions):
    // Galaxy Store's install takes no acceptance of ours, and its distribution guide (3.1.2) wants the
    // privacy policy shown in the app. With no updater options there is no updater, so no feed is read,
    // no notice shows and the drawer has no "Check for update": Galaxy Store updates the app itself.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        return ClientAppOptions.Create(context, ClientAppConfigs.Load());
    }
}