using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Linux;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Cli.Linux;

// A Linux head's whole entry point: the machine facts Linux answers (CliPlatform) joined to the
// product facts the head answers (CliHeadParams), and the words an earlier installer's launcher
// script or systemd unit may still be passing, caught before the parser sees them.
public static class LinuxCliHost
{
    public static async Task<int> Run(string[] args, CliHeadParams head, CancellationToken cancellationToken)
    {
        var paths = new LinuxCliPaths();
        var platform = new CliPlatform {
            Paths = paths,
            Instance = new LinuxInstanceController(paths),
            CreateDaemonHost = () => new LinuxDaemonHost(head.AppOptionsFactory, paths.InstanceName)
        };

        var legacyExitCode = await TryRunLegacy(args, head, platform, cancellationToken).Vhc();
        return legacyExitCode ?? await CliHost.Run(args, head, platform, cancellationToken).Vhc();
    }

    // Null when the arguments are not one of the old forms, which means the parser gets them.
    //
    // "stop" reaches the running service through its own command file rather than through
    // systemctl, because the systemd unit's ExecStop is what calls it: asking systemd to stop a
    // unit from inside that unit's own stop step is a deadlock.
    private static async Task<int?> TryRunLegacy(IReadOnlyList<string> args, CliHeadParams head,
        CliPlatform platform, CancellationToken cancellationToken)
    {
        if (args.Count > 0 && args[0].Equals("stop", StringComparison.OrdinalIgnoreCase)) {
            try {
                VpnHoodAppLinux.Init(head.AppOptionsFactory, ["stop"]);
                return 0;
            }
            catch (GracefullyShutdownException) {
                return 0; // the stop was delivered, which is this command's whole job
            }
            catch (Exception ex) {
                await Console.Error.WriteLineAsync(ex.Message).Vhc();
                return 1;
            }
        }

        // An old launcher's flags. Both meant "run the app without showing anything", which is now
        // the service's job and not this process's.
        if (args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase) ||
                          x.Equals("/autoconnect", StringComparison.OrdinalIgnoreCase))) {
            VhLogger.Instance.LogInformation(
                "/nowindow is the {InstanceName} service now. Starting it instead of a window.",
                platform.Paths.InstanceName);
            return await platform.Instance.Start(cancellationToken).Vhc();
        }

        return null;
    }
}
