using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using Windows.ApplicationModel.Core;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation;

// The VPN host's whole Main, which a head's VPN host exe calls on its MTA thread. Windows starts the exe as
// the package's background-task server; a plain console host or a direct COM registration does not activate
// the VPN background task, CoreApplication.RunWithActivationFactories does.
public static class WindowsVpnPluginHost
{
    private const string LogFileName = "vpn-host.log";

    public static void Run([ReadOnlyArray] string[] args)
    {
        // the process's own log from its first line, as the iOS extension has os_log: what comes before the
        // VPN service opens its log, an activation's failure among it
        var folder = WindowsVpnPluginFolders.VpnServiceFolder;
        Directory.CreateDirectory(folder);
        var fileLogger = new FileLogger(Path.Combine(folder, LogFileName)) { AutoFlush = true };
        fileLogger.Open(deleteOld: true);
        VhLogger.AddProvider(new TextLoggerProvider(fileLogger));

        VhLogger.Instance.LogInformation("The VPN host started on .NET {Version}. Args: {Args}",
            Environment.Version, string.Join(' ', args));
        try {
            CoreApplication.RunWithActivationFactories(new ActivationFactoryProvider());
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The VPN host failed.");
            throw;
        }
    }
}
