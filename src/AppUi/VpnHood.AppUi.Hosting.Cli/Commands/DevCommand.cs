using System.CommandLine;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// The daemon and the window in this one process, for a debugger: a breakpoint in the app, its tunnel
// or its UI is reached from one run, with no service installed. The window reaches the app over
// loopback as it reaches the service, so what runs is the real path but for the process boundary.
// The app keeps its storage in the person's own folder and runs as whoever started it.
internal static class DevCommand
{
    public static Command Create(CliPlatform platform, CliInitParams initParams, MainThreadQueue mainThread)
    {
        var command = new Command("dev", "Run the service and the window in this one process, for a debugger.") {
            Hidden = true
        };

        command.SetAction((_, cancellationToken) => Run(platform, initParams, mainThread, cancellationToken));

        return command;
    }

    private static async Task<int> Run(CliPlatform platform, CliInitParams initParams, MainThreadQueue mainThread,
        CancellationToken cancellationToken)
    {
        try {
            await using var daemonHost = platform.DaemonHostFactory.CreateDev(platform.Paths.DevStoragePath);
            var localWebHost = VpnHoodApp.Instance.LocalWebHost ??
                               throw new InvalidOperationException(
                                   "The app has no local web host, so the window could not reach it.");
            await localWebHost.EnsureStarted(cancellationToken).Vhc();

            // No channel to ask: the app is here, and so is the process the window's calls must reach.
            await using var connection = DaemonConnection.CreateInProcess(localWebHost, platform.PeerCheck,
                platform.Instance, platform.Paths.InstanceName);
            await UiCommand.RunWindow(platform, initParams, mainThread, connection,
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
