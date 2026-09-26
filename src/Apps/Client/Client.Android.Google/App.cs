using Android.Runtime;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Hosting.Avalonia.Android;
using VpnHood.AppLib.App.Android.Constants;
using VpnHood.AppLib.Stores.GooglePlay;
using VpnHood.AppLib.App.Services.Updaters;

namespace VpnHood.App.Client.Android.Google;

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
            StorageFolderName = "VpnHood", // what every shipped build has used
            AppOptionsFactory = CreateAppOptions
        };
    }

    // The product's options, and Google Play's lines on top.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        var appConfigs = ClientAppConfigs.Load();
        // this head's own: the Google Play build is the only Client that reports usage
        appConfigs.Ga4MeasurementId = "G-4LE99XKZYE";
        var options = ClientAppOptions.Create(context, appConfigs);
        // The store already took this acceptance at install - see AppOptions.
        options.IsLicenseAgreementRequired = false;
        options.UserReviewProvider = new GooglePlayInAppUserReviewProvider();
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = appConfigs.GetUpdateInfoUrl(AppConstants.PackageTitle, "android"),
            UpdaterProvider = new GooglePlayAppUpdaterProvider(),
            PromptDelay = TimeSpan.FromDays(3)
        };
        return options;
    }
}