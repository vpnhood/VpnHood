using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.WindowsVpnPlugin;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;

// A Windows VPN plug-in head's whole entry point: the app and its window in the head's process
// (InProcessDesktopHost), with the machine facts the package answers (WindowsVpnPluginDesktopPlatform).
// The same synchronous call as every desktop host's, run on the head's [STAThread] main thread, which
// the window gets.
public static class WindowsVpnPluginDesktopHost
{
    private const string LogFileName = "window.log";

    // args: none is acted on yet - the edition's command line waits (docs/todo.md); the log keeps them
    public static int Run(string[] args, DesktopInitParams initParams)
    {
        // A debugger's sink, and the process's own file from its first line, as the VPN host keeps one:
        // the window has no console, and the Event Log no source here, which only an administrator
        // registers.
        VhLogger.AddProvider(new TraceLoggerProvider());
        var fileLogger = new FileLogger(Path.Combine(WindowsVpnPluginFolders.LocalFolder, LogFileName)) { AutoFlush = true };
        fileLogger.Open(deleteOld: true);
        VhLogger.AddProvider(new TextLoggerProvider(fileLogger));

        VhLogger.Instance.LogInformation("The window started on .NET {Version}. Args: {Args}",
            Environment.Version, string.Join(' ', args));
        return InProcessDesktopHost.Run(initParams, new WindowsVpnPluginDesktopPlatform(initParams));
    }
}
