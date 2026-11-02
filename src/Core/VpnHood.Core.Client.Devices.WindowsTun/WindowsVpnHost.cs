using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.WindowsTun;

// The VPN host's whole Main, which a head's VPN host executable calls: the VPN service in a process of its own,
// with Core and nothing of the app, as the phones and the Store run it. The service starts it at a connect
// (WindowsVpnHostProcess) and it ends with its session; the app reaches it as it reaches any VPN service, over the
// channel and the files in the storage's VPN service folder.
public static class WindowsVpnHost
{
    internal const string StorageOption = "--storage";
    internal const string ServiceOption = "--service";

    public static async Task<int> Run(IReadOnlyList<string> args)
    {
        if (args is not [StorageOption, var storageFolder, ServiceOption, var serviceName]) {
            var usage = $"The VPN host runs as its service starts it: {StorageOption} <folder> {ServiceOption} <name>";
            await Console.Error.WriteLineAsync(usage.AsMemory(), CancellationToken.None).Vhc();
            return 2;
        }

        // The service's sink from the first line, under its name: what fails before the VPN service opens its own
        // log, at the start request, is told where the service's own warnings and errors are.
        VhLogger.AddProvider(new WinEventLogLoggerProvider(serviceName));
        try {
            using var vpnService = new WindowsVpnService(WindowsDevice.GetVpnServiceConfigFolder(storageFolder),
                new WindowsVpnKillSwitch(owner: storageFolder, serviceName), withLogger: true);
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
