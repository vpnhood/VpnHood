using System.CommandLine;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.WebHosting;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Channel;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Commands;

// The one process that holds a VpnHoodApp, on a platform where that process is this binary run
// headless. It shows nothing - a display belongs to a session, and this starts before anyone has
// logged in - and serves its API on loopback, which is how the window and the commands reach it.
//
// The platform builds the app (IAppDaemonHost), which is starting it; this binds the API, opens the
// channel, and waits. It ends when the service manager stops it - by a signal the parser turns into
// cancellation (DesktopHost gives the stop time to disconnect), or by a call the platform's host turns
// into the same (IDesktopPlatform.HostDaemon) - or when the app disposes itself.
internal static class DaemonCommand
{
    public static Command Create(IDesktopPlatform platform)
    {
        var command = new Command("daemon",
            "Run the VPN service in the foreground. This is what the system's service manager starts.");

        command.SetAction((_, cancellationToken) => platform.HostDaemon is { } hostDaemon
            ? hostDaemon(runCancellationToken => Run(platform, runCancellationToken), cancellationToken)
            : Run(platform, cancellationToken));

        return command;
    }

    private static async Task<int> Run(IDesktopPlatform platform, CancellationToken cancellationToken)
    {
        // The platform's console from the first line: a terminal, or the journal. Under the service
        // control manager there is none, and its host has added the Event Log.
        VhLogger.AddProvider(platform.CreateConsoleLoggerProvider());

        IAppDaemonHost daemonHost;
        try {
            // Building it is starting it; a platform that cannot says why, and that is the answer.
            daemonHost = platform.DaemonHostFactory.CreateService();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError("{Message}", ex.Message);
            return 1;
        }

        DaemonChannelServer? channel = null;
        IAppWebHost? localWebHost = null;
        EventHandler onRestarted = (_, _) => Publish(channel, localWebHost);
        try {
            // Bind now rather than wait for the first caller: the window and the commands ask the
            // channel for the address, and an instance that has not bound is one they call dead.
            localWebHost = VpnHoodApp.Instance.LocalWebHost ??
                           throw new InvalidOperationException(
                               "The daemon has no local web host, so nothing could reach it.");
            var url = await localWebHost.EnsureStarted(cancellationToken).Vhc();

            // never the address as it is: it carries the token, and the log is read by more than
            // administrators
            VhLogger.Instance.LogInformation("{InstanceName} is listening on {ApiUrl}",
                platform.Paths.InstanceName, url.GetLeftPart(UriPartial.Authority));

            // Heard before the first answer is made, and told once more after: a rebind meanwhile
            // found no channel to tell.
            localWebHost.Restarted += onRestarted;
            channel = await DaemonChannelServer.TryStart(platform.Channel,
                Answer(localWebHost) ?? throw new InvalidOperationException("The local web host has no address."),
                platform.AdministratorsOnlyMessage, cancellationToken).Vhc();
            Publish(channel, localWebHost);

            // until the app is disposed - by the system's signal, or by itself
            while (VpnHoodApp.IsInit && !cancellationToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            VhLogger.Instance.LogInformation("Exit requested.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "{InstanceName} could not start.", platform.Paths.InstanceName);
            return 1;
        }
        finally {
            if (localWebHost != null)
                localWebHost.Restarted -= onRestarted;
            if (channel != null)
                await channel.DisposeAsync().Vhc();
            await daemonHost.DisposeAsync().Vhc();
        }

        return 0;
    }

    private static void Publish(DaemonChannelServer? channel, IAppWebHost? localWebHost)
    {
        if (channel != null && localWebHost != null && Answer(localWebHost) is { } answer)
            channel.Publish(answer);
    }

    // Where the API is now, token and all; null while the host has no address, as it stops.
    private static DaemonChannelAnswer? Answer(IAppWebHost localWebHost)
    {
        var urls = localWebHost.Urls;
        return urls.Count == 0
            ? null
            : new DaemonChannelAnswer {
                ApiUrl = urls[0],
                ProcessId = Environment.ProcessId,
                Version = DaemonChannelAnswer.CurrentVersion
            };
    }
}
