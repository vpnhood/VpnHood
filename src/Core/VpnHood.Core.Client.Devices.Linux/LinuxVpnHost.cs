using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Linux;

// The VPN host's whole Main, which a head's VPN host binary calls: the VPN service in a process of its own, with
// Core and nothing of the app, as the phones and the Store run it. The daemon starts it at a connect
// (LinuxVpnHostProcess) and it ends with its session; the app reaches it as it reaches any VPN service, over the
// channel and the files in the storage's VPN service folder. It is in the unit's cgroup, so the kill switch lets
// it out as it does the daemon, and the unit's stop reaches it with the daemon's.
public static class LinuxVpnHost
{
    // A signal's graceful close, within the daemon's own (DesktopHost), which leaves its quick one the rest.
    private static readonly TimeSpan SignalGracefulCloseTimeout = TimeSpan.FromSeconds(8);

    public static async Task<int> Run(IReadOnlyList<string> args)
    {
        if (LinuxVpnHostArgs.TryParse(args) is not { } hostArgs) {
            var usage = $"The VPN host runs as its daemon starts it: {LinuxVpnHostArgs.Usage}";
            await Console.Error.WriteLineAsync(usage.AsMemory(), CancellationToken.None).Vhc();
            return 2;
        }

        // The daemon's sink from the first line: the journal, the unit's stdout, which the daemon hands its child
        // too; a terminal where the daemon runs in one. What fails before the VPN service opens its own log, at the
        // start request, is told where the daemon's own lines are.
        VhLogger.AddProvider(LinuxJournalLogger.IsConsole ? new LinuxJournalLoggerProvider() : new ConsoleLoggerProvider());
        try {
            using var vpnService = new LinuxVpnService(LinuxDevice.GetVpnServiceConfigFolder(hostArgs.StorageFolder),
                new LinuxVpnKillSwitch(owner: hostArgs.StorageFolder), withLogger: true);

            // A signal - the unit's stop, Ctrl+C, a terminal's hang-up - ends the session as a disconnect does, and
            // the process with it; the signals are held for the run, as a collected one unregisters.
            using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => OnSignal(context, vpnService));
            using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => OnSignal(context, vpnService));
            using var sigHup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => OnSignal(context, vpnService));

            vpnService.OnConnect();
            await vpnService.Stopped.Vhc();
            return 0;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The VPN host failed.");
            throw;
        }
    }

    // the signal is held, so the process stays until its session has ended
    private static void OnSignal(PosixSignalContext context, LinuxVpnService vpnService)
    {
        context.Cancel = true;
        VhLogger.Instance.LogInformation("The VPN host has received the {Signal} signal.", context.Signal);
        _ = Close(vpnService);
    }

    // Rule 15: the graceful close up to its deadline, then the quick one, whose end ends the run.
    private static async Task Close(LinuxVpnService vpnService)
    {
        try {
            await vpnService.TryDisconnect().WaitAsync(SignalGracefulCloseTimeout).Vhc();
        }
        catch (TimeoutException) {
            VhLogger.Instance.LogWarning("The VPN host's session did not end in {Timeout}, so it ends at once.",
                SignalGracefulCloseTimeout);
        }

        vpnService.Dispose();
    }
}
