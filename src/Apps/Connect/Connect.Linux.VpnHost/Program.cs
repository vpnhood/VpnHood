using VpnHood.Core.Client.Devices.Linux;

namespace VpnHood.App.Connect.Linux.VpnHost;

// The app's VPN host, which its daemon starts at each connect. Product-neutral: all of it is LinuxVpnHost.Run.
internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        return LinuxVpnHost.Run(args);
    }
}
