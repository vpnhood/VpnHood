using System.CommandLine;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// The one process that holds a VpnHoodApp, on a platform where that process is this binary run
// headless. It shows nothing - a display belongs to a session, and this starts before anyone has
// logged in - and serves its API on loopback, which is how the window and the commands reach it.
//
// The platform builds the app (IAppDaemonHost) and says what it needs to; this binds the API,
// publishes the address, and waits. It ends when the instance is stopped, when the platform's
// stop command arrives, or when the app disposes itself.
internal static class DaemonCommand
{
    public static Command Create(CliPlatform platform, Func<IAppDaemonHost> createDaemonHost)
    {
        var command = new Command("daemon",
            "Run the VPN service in the foreground. This is what the system's service unit starts.");

        command.SetAction((_, cancellationToken) => Run(platform, createDaemonHost, cancellationToken));
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
            await daemonHost.Prepare(cancellationToken).Vhc();

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

            // until the app is disposed - by the stop command, by the system's signal, or by itself
            while (VpnHoodApp.IsInit && !cancellationToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            VhLogger.Instance.LogInformation("Exit requested.");
        }
        finally {
            DaemonInfo.Delete(daemonInfoFilePath);
            daemonHost.Dispose();
        }

        return 0;
    }
}
