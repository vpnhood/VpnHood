using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Windows;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Windows;

// A Windows head's whole entry point: the machine facts Windows answers (DesktopPlatform) joined to the
// product facts the head answers. The same synchronous call as every desktop host's, run on the
// head's [STAThread] main thread, which the window gets (DesktopHost.Run).
//
// The app runs in a LocalSystem service, "daemon" under the service control manager; the window and
// its tray run as whoever is signed in, not elevated, and reach the service over loopback.
public static class WindowsDesktopHost
{
    public static int Run(string[] args, DesktopInitParams initParams)
    {
        // every run logs to a debugger; the console is the daemon's and dev's, the Event Log the
        // service's and the window's
        VhLogger.AddProvider(new TraceLoggerProvider());

        var paths = new WindowsDesktopPaths(initParams.AppId);
        var setup = new WindowsServiceSetup(paths, initParams.AppName);
        var channel = new WindowsDaemonChannel(paths);
        var instance = new WindowsInstanceController(paths, setup, channel);
        var platform = new DesktopPlatform {
            Paths = paths,
            Instance = instance,
            Channel = channel,
            PeerCheck = new WindowsLoopbackPeerCheck(),
            IsAdministrator = WindowsAdministrators.IsCurrentUser,
            InstanceSetup = setup,
            CreateTray = trayParams => WindowsAppTray.Start(trayParams.Api, trayParams.UiAssets,
                trayParams.ShowWindow, trayParams.Exit),
            DaemonHostFactory = new WindowsDaemonHostFactory(initParams, paths),
            CreateConsoleLoggerProvider = () => new ConsoleLoggerProvider(),
            CreateSystemLogLoggerProvider = () => new WinEventLogLoggerProvider(paths.InstanceName),
            HostDaemon = (run, cancellationToken) =>
                WindowsDaemonService.Host(paths.InstanceName, run, cancellationToken)
        };

        return DesktopHost.Run(args, initParams, platform);
    }
}
