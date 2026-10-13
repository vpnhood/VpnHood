using Android.Runtime;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Hosting.Avalonia.Android;
using VpnHood.AppLib.App.Android.Constants;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppLib.Stores.AndroidWeb;

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
            StorageFolderName = "VpnHood", // what every shipped build has used
            AppOptionsFactory = CreateAppOptions
        };
    }

    // The product's options, and this channel's lines on top: the APK from our site.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        var appConfigs = ClientAppConfigs.Load();
        var options = ClientAppOptions.Create(context, appConfigs);

        options.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = appConfigs.GetUpdateInfoUrl(AppConstants.PackageTitle, AndroidWebAppUpdaterProvider.FeedDistribution),
            // the notice downloads the APK and installs it, rather than opening the site; none over a
            // store's install or a copy another key signed, whose notice keeps the site's link
            UpdaterProvider = AndroidWebAppUpdaterProvider.TryCreate(ReleaseCertificateSha256)
        };
        return options;
    }

    // The key our site's APK is signed with: a copy signed with another is never offered the update.
    // A Debug build is signed with the machine's own key, so it takes any.
#if DEBUG
    private const string? ReleaseCertificateSha256 = null;
#else
    private const string ReleaseCertificateSha256 = "07491d8a6547058071691e5d5dffc1a8b2c2584e11f27857f49b58d0a6bb7bf4";
#endif
}