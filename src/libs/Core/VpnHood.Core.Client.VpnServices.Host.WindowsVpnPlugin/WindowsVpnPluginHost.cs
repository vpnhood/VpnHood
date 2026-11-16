using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using Windows.ApplicationModel.Core;
using VpnHood.Core.Client.Devices.WindowsVpnPlugin;

namespace VpnHood.Core.Client.VpnServices.Host.WindowsVpnPlugin;

/// <summary>
/// Provides the VPN host's whole Main, which a head's VPN host executable calls on its MTA thread.
/// </summary>
/// <remarks>
/// Windows starts the executable as the package's background-task server; a plain console host or a direct COM
/// registration does not activate the VPN background task, <c>CoreApplication.RunWithActivationFactories</c> does.
/// </remarks>
public static class WindowsVpnPluginHost
{
    private const string LogFileName = "vpn-host.log";
    private const string ServerNameArgument = "-ServerName:";

    /// <summary>
    /// Runs the VPN host: the server of the package's VPN task and app service, with its own log,
    /// <c>vpn-host.log</c>; or, where a person launched its entry in Start, a view that opens the app for Quick Launch.
    /// </summary>
    /// <param name="args">The command line Windows gives, with the server's name where it starts the server.</param>
    /// <exception cref="InvalidOperationException">The process has no path.</exception>
    /// <exception cref="Exception">The server failed; the error is logged first.</exception>
    public static void Run([ReadOnlyArray] string[] args)
    {
        // A person's launch of the VPN host's own entry in Start, Quick Launch (the package's manifest): Windows starts
        // the server of the VPN's task and the app service by the name the manifest gives it, the host's own
        // executable's, and the entry's launch by another (VpnPluginHost.AppX...mca), which awaits a view. A failure
        // goes to Windows' Application log, as the host's own log is the server's.
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The VPN host has no path.");
        if (!args.Contains(ServerNameArgument + Path.GetFileNameWithoutExtension(processPath), StringComparer.OrdinalIgnoreCase)) {
            CoreApplication.Run(new QuickLaunchView());
            return;
        }

        // the process's own log from its first line, as the iOS extension has os_log: what comes before the
        // VPN service opens its log, an activation's failure among it
        var folder = WindowsVpnPluginFolders.VpnServiceFolder;
        Directory.CreateDirectory(folder);
        var fileLogger = new FileLogger(Path.Combine(folder, LogFileName)) { AutoFlush = true };
        fileLogger.Open(deleteOld: true);
        VhLogger.AddProvider(new TextLoggerProvider(fileLogger));

        VhLogger.Instance.LogInformation("The VPN host started on .NET {Version}. ProcessId: {ProcessId}, Args: {Args}",
            Environment.Version, Environment.ProcessId, string.Join(' ', args));
        try {
            CoreApplication.RunWithActivationFactories(new ActivationFactoryProvider());
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The VPN host failed.");
            throw;
        }
    }
}
