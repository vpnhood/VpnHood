using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Linux;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// A Linux head's whole entry point: the machine facts Linux answers (DesktopPlatform) joined to the
// product facts the head answers (DesktopInitParams). The same synchronous call as every desktop
// host's, run on the head's main thread (DesktopHost.Run).
public static class LinuxDesktopHost
{
    public static int Run(string[] args, DesktopInitParams initParams)
    {
        var paths = new LinuxDesktopPaths(initParams.AppId);
        // No tray keeps a hidden window, so a closed one is gone; the installer registers the unit;
        // systemd's signal stops the daemon; and the daemon's console is the journal where stdout is
        // the journal, so no line reaches it twice.
        var platform = new DesktopPlatform {
            Paths = paths,
            Instance = new LinuxInstanceController(paths),
            Channel = new LinuxDaemonChannel(paths),
            PeerCheck = new LinuxLoopbackPeerCheck(),
            IsAdministrator = LinuxAdministrators.IsCurrentUser,
            DaemonHostFactory = new LinuxDaemonHostFactory(initParams, paths),
            CreateConsoleLoggerProvider = () => LinuxJournalLogger.IsConsole
                ? new LinuxJournalLoggerProvider()
                : new ConsoleLoggerProvider()
        };

        return DesktopHost.Run(args, initParams, platform);
    }
}
