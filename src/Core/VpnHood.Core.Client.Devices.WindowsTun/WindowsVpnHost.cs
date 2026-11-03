using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.WindowsTun;

// The VPN host's whole Main, which a head's VPN host executable calls: the VPN service in a process of its own,
// with Core and nothing of the app, as the phones and the Store run it. The daemon starts it at a connect
// (WindowsVpnHostProcess) and it ends with its session; the app reaches it as it reaches any VPN service, over the
// channel and the files in the storage's VPN service folder.
public static class WindowsVpnHost
{
    public static async Task<int> Run(IReadOnlyList<string> args)
    {
        if (WindowsVpnHostArgs.TryParse(args) is not { } hostArgs) {
            var usage = $"The VPN host runs as its daemon starts it: {WindowsVpnHostArgs.Usage}";
            await Console.Error.WriteLineAsync(usage.AsMemory(), CancellationToken.None).Vhc();
            return 2;
        }

        // The daemon's sink from the first line, under its name: what fails before the VPN service opens its own
        // log, at the start request, is told where the daemon's own warnings and errors are.
        VhLogger.AddProvider(new WinEventLogLoggerProvider(hostArgs.DaemonName));
        try {
            using var vpnService = new WindowsVpnService(WindowsDevice.GetVpnServiceConfigFolder(hostArgs.StorageFolder),
                new WindowsVpnKillSwitch(owner: hostArgs.StorageFolder, hostArgs.DaemonName), withLogger: true);
            vpnService.OnConnect();
            await vpnService.Stopped.Vhc();
            return 0;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The VPN host failed.");
            throw;
        }
    }
}
