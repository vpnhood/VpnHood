using VpnHood.AppUi.Hosting.Desktop.Daemon;
using VpnHood.Core.Client.Devices.Abstractions;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// A Linux head's whole entry point: the machine facts Linux answers (LinuxDesktopPlatform) joined to
// the product facts the head answers (DesktopInitParams). The same synchronous call as every desktop
// host's, run on the head's main thread (DesktopHost.Run).
public static class LinuxDesktopHost
{
    // inProcessVpnServiceFactory: the VPN service dev runs in this process (LinuxVpnServiceFactory), which the head
    // hands in, as only the head references the VPN service's side; the daemon starts the VPN host instead.
    public static int Run(string[] args, DesktopInitParams initParams, IInProcessVpnServiceFactory inProcessVpnServiceFactory)
    {
        return DesktopHost.Run(args, initParams, new LinuxDesktopPlatform(initParams, inProcessVpnServiceFactory));
    }
}
