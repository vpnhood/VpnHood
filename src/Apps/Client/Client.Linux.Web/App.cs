using VpnHood.AppLogic.App;
using VpnHood.AppHosting.Desktop;
using VpnHood.AppHosting.Desktop.Linux;
using VpnHood.AppLogic.App.Services.Updaters;
using VpnHood.AppHosting.Avalonia.Desktop;
using VpnHood.AppUi.Avalonia.Classic;

// ReSharper disable LocalizableElement

namespace VpnHood.App.Client.Linux.Web;

// The Linux head, which is the two answers DesktopHost cannot give: the app to build when this
// process is the service, and the UI to show when it is the window. Everything else - which of the
// three this run is, the commands, the service - is the same on both Linux heads and lives in Hosting/Desktop.
internal static class App
{
    private static int Main(string[] args)
    {
        return LinuxDesktopHost.Run(args, new DesktopInitParams {
            AppId = AppConstants.AppId,
            AppName = AppConstants.AppName,
            AppVersion = Version.Parse(AppConstants.AppVersion),
            AppOptionsFactory = CreateAppOptions,
            IsAddAccessKeySupported = ClientAppOptions.IsAddAccessKeySupported,
            Ui = new AvaloniaDesktopUi<ClassicAvaloniaApp>()
        });
    }

    // The product's options, and this channel's lines on top: the package from our site, whose notice's
    // Update downloads it and Install hands it to the install's own updater; none outside an install.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        var options = ClientAppOptions.Create(context, ClientAppConfigs.Load());
        if (LinuxAppUpdaterProvider.TryCreate(context) is { } updater)
            options.UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = updater.UpdateInfoUrl,
                UpdaterProvider = updater
            };
        return options;
    }
}
