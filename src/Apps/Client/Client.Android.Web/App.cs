using Android.Runtime;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Hosting.Avalonia.Android;
using VpnHood.AppLib.App.Android.Constants;
#if !VH_APP_NO_UPDATE
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppLib.Stores.AndroidWeb;
#endif

namespace VpnHood.App.Client.Android.Web;

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

    // The product's options, and this channel's lines on top: the APK from our site.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        var appConfigs = ClientAppConfigs.Load();
        var options = ClientAppOptions.Create(context, appConfigs);

        // with no options there is no updater: no feed is read, no notice shows, and the drawer has no
        // "Check for update". The repository's own client keeps that build updated.
#if !VH_APP_NO_UPDATE
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = appConfigs.GetUpdateInfoUrl(AppConstants.PackageTitle, AndroidWebAppUpdaterProvider.FeedDistribution),
            // the notice downloads the APK and installs it, rather than opening the site; not over a
            // store's install, whose notice keeps the site's link
            UpdaterProvider = AndroidWebAppUpdaterProvider.Create()
        };
#endif
        return options;
    }
}