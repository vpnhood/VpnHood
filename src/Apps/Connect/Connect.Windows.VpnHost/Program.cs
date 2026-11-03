using VpnHood.Core.Client.VpnServices.Host.WindowsTun;

namespace VpnHood.App.Connect.Windows.VpnHost;

// The app's VPN host, which its daemon starts at each connect. Product-neutral: all of it is WindowsVpnHost.Run.
internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        return WindowsVpnHost.Run(args);
    }
}
