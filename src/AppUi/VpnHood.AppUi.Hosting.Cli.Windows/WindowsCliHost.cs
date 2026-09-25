using VpnHood.AppLib.App.Windows;
using VpnHood.AppUi.Hosting.Cli.Windows.Internal;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

// A Windows head's whole entry point: the machine facts Windows answers (CliPlatform) joined to the
// product facts the head answers, and the words an earlier release's shortcuts may still pass,
// caught before the parser sees them. The same synchronous call as every desktop host's, run on the
// head's [STAThread] main thread, which the window gets (CliHost.Run).
//
// The app runs in a LocalSystem service, "daemon" under the service control manager; the window and
// its tray run as whoever is signed in, not elevated, and reach the service over loopback.
public static class WindowsCliHost
{
    public static int Run(string[] args, CliHeadParams head)
    {
        var paths = new WindowsCliPaths(head.AppId);
        var setup = new WindowsServiceSetup(paths);
        var instance = new WindowsInstanceController(paths, setup);
        var platform = new CliPlatform {
            Paths = paths,
            Instance = instance,
            InstanceSetup = setup,
            CreateTray = trayParams => WindowsAppTray.Start(trayParams.Api, trayParams.UiAssets,
                trayParams.ShowWindow, trayParams.Exit),
            CreateDaemonHost = () => WindowsDaemonHost.CreateService(head, paths),
            CreateDevDaemonHost = storagePath => WindowsDaemonHost.CreateDev(head, storagePath),
            HostDaemon = (run, cancellationToken) =>
                WindowsDaemonService.Host(paths.InstanceName, run, cancellationToken)
        };

        return CliHost.Run(TranslateLegacy(args), head, platform);
    }

    // Migration (2026-09): drop a few months after it ships.
    // The flags an earlier release took. Both meant "run the app without showing anything", which is
    // now the service's job; what is left of them is the tray, with the window closed - and for
    // /autoconnect, a connect.
    private static string[] TranslateLegacy(string[] args)
    {
        var isAutoConnect = args.Any(x => x.Equals("/autoconnect", StringComparison.OrdinalIgnoreCase));
        var isNoWindow = args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase));
        return isAutoConnect ? ["ui", "--tray", "--connect"]
            : isNoWindow ? ["ui", "--tray"]
            : args;
    }
}
