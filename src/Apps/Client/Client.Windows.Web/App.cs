using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppLib.App.WindowsTun;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppUi.Hosting.Desktop;
using VpnHood.AppUi.Hosting.Desktop.WindowsTun;
using VpnHood.AppUi.Presentation.Classic.Avalonia;

namespace VpnHood.App.Client.Windows.Web;

// The Windows head, which is the answers WindowsDesktopHost cannot give: the app to build when this
// process is the service, and the UI to show when it is the window. Everything else - which of the
// three this run is, the commands, the service - is the same on both Windows heads and lives in
// Hosting/Desktop.
internal static class App
{
    [STAThread]
    private static int Main(string[] args)
    {
        return WindowsDesktopHost.Run(args, new DesktopInitParams {
            AppId = AppConstants.AppId,
            AppName = AppConstants.AppName,
            AppVersion = Version.Parse(AppConstants.AppVersion),
            AppOptionsFactory = CreateAppOptions,
            IsAddAccessKeySupported = ClientAppOptions.IsAddAccessKeySupported,
            Ui = new AvaloniaDesktopUi<ClassicAvaloniaApp>()
        });
    }

    // The product's options, and this channel's lines on top: the installer from our site, whose
    // newer version the app announces with its download link.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        // the folder every release before the service used; the service's first start copies it in
#pragma warning disable CS0618 // Type or member is obsolete
        // ReSharper disable once HeuristicUnreachableCode
        LegacyStorageMigration.TryImport(context.StoragePath, AppConstants.IsDebugMode ? "VpnHoodClient.debug" : "VpnHood");
#pragma warning restore CS0618 // Type or member is obsolete
        var appConfigs = ClientAppConfigs.Load();
        var options = ClientAppOptions.Create(context, appConfigs);
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = appConfigs.GetUpdateInfoUrl(AppConstants.PackageTitle, "win-x64")
        };
        return options;
    }
}
