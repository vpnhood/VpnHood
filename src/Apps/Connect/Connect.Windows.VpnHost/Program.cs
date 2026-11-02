using VpnHood.Core.Client.Devices.WindowsTun;

namespace VpnHood.App.Connect.Windows.VpnHost;

// The app's VPN host, which its service starts at each connect. Product-neutral: all of it is WindowsVpnHost.Run.
internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        return WindowsVpnHost.Run(args);
    }
}
