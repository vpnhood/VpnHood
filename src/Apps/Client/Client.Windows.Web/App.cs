using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppUi.Hosting.Cli;
using VpnHood.AppUi.Hosting.Cli.Windows;
using VpnHood.AppUi.Presentation.Classic.Avalonia;

namespace VpnHood.App.Client.Windows.Web;

// The Windows head, which is the answers WindowsCliHost cannot give: the app to build when this
// process is the service, and the UI to show when it is the window. Everything else - which of the
// three this run is, the commands, the service - is the same on both Windows heads and lives in
// Hosting/Cli.
internal static class App
{
    [STAThread]
    private static int Main(string[] args)
    {
        return WindowsCliHost.Run(args, new CliHeadParams {
            AppId = AppConstants.AppId,
            AppOptionsFactory = CreateAppOptions,
            IsAddAccessKeySupported = ClientAppOptions.IsAddAccessKeySupported,
            Ui = new AvaloniaDesktopHost<ClassicAvaloniaApp>()
        });
    }

    // The product's options, and this channel's lines on top: the installer from our site, whose
    // newer version the app announces with its download link.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        // the folder every release before the service used; the service's first start copies it in
        // ReSharper disable once HeuristicUnreachableCode
        LegacyStorage.TryImport(context.StoragePath, AppConstants.IsDebugMode ? "VpnHoodClient.debug" : "VpnHood");
        var appConfigs = ClientAppConfigs.Load();
        var options = ClientAppOptions.Create(context, appConfigs);
        options.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = appConfigs.GetUpdateInfoUrl(AppConstants.PackageTitle, "win-x64"),
            PromptDelay = TimeSpan.Zero
        };
        return options;
    }
}
