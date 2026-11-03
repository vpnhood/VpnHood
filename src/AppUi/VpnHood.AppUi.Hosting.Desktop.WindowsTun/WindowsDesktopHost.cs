using VpnHood.AppUi.Hosting.Desktop.Daemon;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// A Windows head's whole entry point: the machine facts Windows answers (WindowsDesktopPlatform)
// joined to the product facts the head answers. The same synchronous call as every desktop host's,
// run on the head's [STAThread] main thread, which the window gets (DesktopHost.Run).
public static class WindowsDesktopHost
{
    // inProcessVpnServiceFactory: the VPN service dev runs in this process (WindowsVpnServiceFactory), which the head
    // hands in, as only the head references the VPN service's side; the daemon starts the VPN host instead.
    public static int Run(string[] args, DesktopInitParams initParams, IInProcessVpnServiceFactory inProcessVpnServiceFactory)
    {
        // every run logs to a debugger; the console is the daemon's and dev's, the Event Log the
        // service's and the window's
        VhLogger.AddProvider(new TraceLoggerProvider());
        return DesktopHost.Run(args, initParams, new WindowsDesktopPlatform(initParams, inProcessVpnServiceFactory));
    }
}
