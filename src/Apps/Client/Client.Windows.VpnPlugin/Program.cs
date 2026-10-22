using VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation;

namespace VpnHood.App.Client.Windows.VpnPlugin;

// The package's VPN host, which Windows starts as its background tasks' server when the app's VPN
// profile connects. Product-neutral: all of it is WindowsVpnPluginHost.Run, on the MTA thread.
internal static class Program
{
    [MTAThread]
    private static void Main(string[] args)
    {
        WindowsVpnPluginHost.Run(args);
    }
}
