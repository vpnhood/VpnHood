using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;

// A Windows VPN plug-in head's whole entry point: the app and its window in the head's process
// (InProcessDesktopHost), with the machine facts the package answers (WindowsVpnPluginDesktopPlatform).
// The same synchronous call as every desktop host's, run on the head's [STAThread] main thread, which
// the window gets.
public static class WindowsVpnPluginDesktopHost
{
    public static int Run(string[] args, DesktopInitParams initParams)
    {
        // every run logs to a debugger; the file is the window's (WindowsVpnPluginDesktopPlatform)
        VhLogger.AddProvider(new TraceLoggerProvider());
        return InProcessDesktopHost.Run(args, initParams, new WindowsVpnPluginDesktopPlatform(initParams));
    }
}
