using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Cli.Linux;

// A Linux head's whole entry point: the machine facts Linux answers (CliPlatform) joined to the
// product facts the head answers (CliHeadParams), and the words an earlier installer's launcher
// script or systemd unit may still be passing, caught before the parser sees them. The same
// synchronous call as every desktop host's, run on the head's main thread (CliHost.Run).
public static class LinuxCliHost
{
    public static int Run(string[] args, CliHeadParams head)
    {
        var paths = new LinuxCliPaths(head.AppId);
        // No tray keeps a hidden window, so a closed one is gone; the installer registers the unit;
        // and systemd's signal stops the daemon.
        var platform = new CliPlatform {
            Paths = paths,
            Instance = new LinuxInstanceController(paths),
            CreateDaemonHost = () => LinuxDaemonHost.CreateService(head, paths),
            CreateDevDaemonHost = storagePath => LinuxDaemonHost.CreateDev(head, storagePath)
        };

        return TryRunLegacy(args, platform) ?? CliHost.Run(args, head, platform);
    }

    // Migration (2026-09): drop a few months after it ships.
    // Null when the arguments are not one of the old forms, which means the parser gets them.
    private static int? TryRunLegacy(IReadOnlyList<string> args, CliPlatform platform)
    {
        // What a unit written by an older installer runs as its ExecStop. There is nothing left for
        // it to do: after ExecStop systemd sends the daemon SIGTERM, which stops it - tunnel first -
        // as it stops a unit the current installer wrote, which has no ExecStop at all.
        if (args.Count > 0 && args[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
            return 0;

        // An old launcher's flags. Both meant "run the app without showing anything", which is now
        // the service's job and not this process's. Nothing else runs yet, so the main thread may
        // wait for the start.
        if (args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase) ||
                          x.Equals("/autoconnect", StringComparison.OrdinalIgnoreCase))) {
            VhLogger.Instance.LogInformation(
                "/nowindow is the {InstanceName} service now. Starting it instead of a window.",
                platform.Paths.InstanceName);
            return platform.Instance.Start(CancellationToken.None).GetAwaiter().GetResult();
        }

        return null;
    }
}
