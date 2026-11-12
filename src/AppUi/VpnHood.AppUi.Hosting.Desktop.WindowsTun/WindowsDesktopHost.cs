using VpnHood.AppUi.Hosting.Desktop.Daemon;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

/// <summary>
/// Provides a Windows head's whole entry point: the machine facts Windows answers joined to the product facts the head
/// answers.
/// </summary>
public static class WindowsDesktopHost
{
    /// <summary>
    /// Runs what the command line asks for, and blocks until it ends: the work runs off the calling thread and the UI on
    /// it, so a head calls it from its <c>[STAThread]</c> <c>Main</c>.
    /// </summary>
    /// <param name="args">The process's command-line arguments.</param>
    /// <param name="initParams">The head's init params.</param>
    /// <returns>The process's exit code.</returns>
    /// <remarks>Every run logs to an attached debugger too.</remarks>
    /// <exception cref="InvalidOperationException">The entry assembly has no name, so the install has none.</exception>
    public static int Run(string[] args, DesktopInitParams initParams)
    {
        // every run logs to a debugger; the console is the daemon's and dev's, the Event Log the
        // service's and the window's
        VhLogger.AddProvider(new TraceLoggerProvider());
        return DesktopHost.Run(args, initParams, new WindowsDesktopPlatform(initParams));
    }
}
