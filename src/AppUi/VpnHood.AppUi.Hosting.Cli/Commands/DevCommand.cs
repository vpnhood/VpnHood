using System.CommandLine;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Cli.Internal;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// The daemon and the window in this one process, for a debugger: a breakpoint in the app, its tunnel
// or its UI is reached from one run, with no service installed. The window reaches the app over
// loopback as it reaches the service, so what runs is the real path but for the process boundary.
// The app keeps its storage in the person's own folder and runs as whoever started it.
internal static class DevCommand
{
    public static Command Create(CliPlatform platform, CliHeadParams head, MainThreadQueue mainThread,
        Func<string, IAppDaemonHost> createDevDaemonHost)
    {
        var command = new Command("dev", "Run the service and the window in this one process, for a debugger.") {
            Hidden = true
        };

        command.SetAction((_, cancellationToken) =>
            Run(platform, head, mainThread, createDevDaemonHost, cancellationToken));

        return command;
    }

    private static async Task<int> Run(CliPlatform platform, CliHeadParams head, MainThreadQueue mainThread,
        Func<string, IAppDaemonHost> createDevDaemonHost, CancellationToken cancellationToken)
    {
        try {
            await using var daemonHost = createDevDaemonHost(platform.Paths.DevStoragePath);
            var localWebHost = VpnHoodApp.Instance.LocalWebHost ??
                               throw new InvalidOperationException(
                                   "The app has no local web host, so the window could not reach it.");
            var url = await localWebHost.EnsureStarted(cancellationToken).Vhc();

            using var connection = new DaemonConnection(new Uri(url.GetLeftPart(UriPartial.Authority)));
            await UiCommand.RunWindow(platform, head, mainThread, connection,
                startHidden: false, connect: false, cancellationToken).Vhc();
            return 0;
        }
        catch (OperationCanceledException) {
            return 130;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "{InstanceName} could not run.", platform.Paths.InstanceName);
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return 1;
        }
    }
}
