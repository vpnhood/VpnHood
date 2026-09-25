using System.CommandLine;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// The one process that holds a VpnHoodApp, on a platform where that process is this binary run
// headless. It shows nothing - a display belongs to a session, and this starts before anyone has
// logged in - and serves its API on loopback, which is how the window and the commands reach it.
//
// The platform builds the app (IAppDaemonHost), which is starting it; this binds the API, publishes
// the address, and waits. It ends when the service manager stops it - by a signal the parser turns
// into cancellation (CliHost gives the stop time to disconnect), or by a call the platform's host
// turns into the same (CliPlatform.HostDaemon) - or when the app disposes itself.
internal static class DaemonCommand
{
    public static Command Create(CliPlatform platform, Func<IAppDaemonHost> createDaemonHost)
    {
        var command = new Command("daemon",
            "Run the VPN service in the foreground. This is what the system's service manager starts.");

        command.SetAction((_, cancellationToken) => platform.HostDaemon is { } hostDaemon
            ? hostDaemon(runCancellationToken => Run(platform, createDaemonHost, runCancellationToken), cancellationToken)
            : Run(platform, createDaemonHost, cancellationToken));

        return command;
    }

    private static async Task<int> Run(CliPlatform platform, Func<IAppDaemonHost> createDaemonHost,
        CancellationToken cancellationToken)
    {
        IAppDaemonHost daemonHost;
        try {
            // Building it is starting it; a platform that cannot says why, and that is the answer.
            daemonHost = createDaemonHost();
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return 1;
        }

        var daemonInfoFilePath = platform.Paths.DaemonInfoFilePath;
        try {
            // Bind now rather than wait for the first caller: the window and the commands look for
            // the address this publishes, and an instance that has not bound is one they call dead.
            var localWebHost = VpnHoodApp.Instance.LocalWebHost ??
                               throw new InvalidOperationException(
                                   "The daemon has no local web host, so nothing could reach it.");
            var url = await localWebHost.EnsureStarted(cancellationToken).Vhc();
            var apiUrl = new Uri(url.GetLeftPart(UriPartial.Authority)); // a browser's URL carries a cache-buster

            await DaemonInfo.Write(daemonInfoFilePath, apiUrl, cancellationToken).Vhc();
            VhLogger.Instance.LogInformation("{InstanceName} is listening on {ApiUrl}",
                platform.Paths.InstanceName, apiUrl);

            // until the app is disposed - by the system's signal, or by itself
            while (VpnHoodApp.IsInit && !cancellationToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            VhLogger.Instance.LogInformation("Exit requested.");
        }
        catch (Exception ex) {
            // in the app's log while it is still open, and where a person or a service manager reads
            VhLogger.Instance.LogError(ex, "{InstanceName} could not start.", platform.Paths.InstanceName);
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return 1;
        }
        finally {
            DaemonInfo.Delete(daemonInfoFilePath);
            await daemonHost.DisposeAsync().Vhc();
        }

        return 0;
    }
}
