using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;

/// <summary>
/// Provides a Windows VPN plug-in head's whole entry point: the app and its window in the head's process, with the
/// machine facts the package answers.
/// </summary>
public static class WindowsVpnPluginDesktopHost
{
    /// <summary>
    /// Runs the app and its window until the window's run ends: the app on a worker thread, and the window on the calling
    /// thread, which must be the head's <c>[STAThread]</c> main thread.
    /// </summary>
    /// <param name="args">The process's command-line arguments.</param>
    /// <param name="initParams">The head's init params.</param>
    /// <returns>
    /// The process's exit code: 0, or 1 where the run failed or an access key it was given could not be read.
    /// </returns>
    /// <remarks>
    /// A second launch hands its access key or Quick Launch to the running one, and returns. Where the app or its window
    /// fails to start, the failure's message shows in the window's place; any failure is logged, and the exit code is 1.
    /// </remarks>
    public static int Run(string[] args, DesktopInitParams initParams)
    {
        // every run logs to a debugger; the file is the window's (WindowsVpnPluginDesktopPlatform)
        VhLogger.AddProvider(new TraceLoggerProvider());
        return InProcessDesktopHost.Run(args, initParams, new WindowsVpnPluginDesktopPlatform(initParams));
    }
}
