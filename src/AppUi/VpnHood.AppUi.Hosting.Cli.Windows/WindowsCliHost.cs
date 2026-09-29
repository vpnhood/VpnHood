using VpnHood.AppLib.App.Windows;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

// A Windows head's whole entry point: the machine facts Windows answers (CliPlatform) joined to the
// product facts the head answers. The same synchronous call as every desktop host's, run on the
// head's [STAThread] main thread, which the window gets (CliHost.Run).
//
// The app runs in a LocalSystem service, "daemon" under the service control manager; the window and
// its tray run as whoever is signed in, not elevated, and reach the service over loopback.
public static class WindowsCliHost
{
    public static int Run(string[] args, CliInitParams initParams)
    {
        var paths = new WindowsCliPaths(initParams.AppId);
        var setup = new WindowsServiceSetup(paths);
        var channel = new WindowsDaemonChannel(paths);
        var instance = new WindowsInstanceController(paths, setup, channel);
        var platform = new CliPlatform {
            Paths = paths,
            Instance = instance,
            Channel = channel,
            PeerCheck = new WindowsLoopbackPeerCheck(),
            InstanceSetup = setup,
            CreateTray = trayParams => WindowsAppTray.Start(trayParams.Api, trayParams.UiAssets,
                trayParams.ShowWindow, trayParams.Exit),
            DaemonHostFactory = new WindowsDaemonHostFactory(initParams, paths),
            HostDaemon = (run, cancellationToken) =>
                WindowsDaemonService.Host(paths.InstanceName, run, cancellationToken)
        };

        return CliHost.Run(args, initParams, platform);
    }
}
