using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop;
using VpnHood.AppUi.Hosting.Desktop.Linux;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppUi.Presentation.Classic.Avalonia;

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
            AppOptionsFactory = CreateAppOptions,
            IsAddAccessKeySupported = ClientAppOptions.IsAddAccessKeySupported,
            Ui = new AvaloniaDesktopUi<ClassicAvaloniaApp>()
        });
    }

    // The product's options, and this channel's lines on top: the package from our site, which
    // updates itself, so the app names no update feed.
    private static AppOptions CreateAppOptions(AppOptionsContext context)
    {
        var appConfigs = ClientAppConfigs.Load();
        var options = ClientAppOptions.Create(context, appConfigs);
        options.UpdaterOptions = new AppUpdaterOptions {
            PromptDelay = TimeSpan.Zero
        };
        return options;
    }
}
