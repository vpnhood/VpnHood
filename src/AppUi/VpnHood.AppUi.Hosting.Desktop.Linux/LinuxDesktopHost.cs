using VpnHood.AppUi.Hosting.Desktop.Daemon;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

/// <summary>
/// Provides a Linux head's whole entry point: the machine facts Linux answers joined to the product facts the head
/// answers.
/// </summary>
public static class LinuxDesktopHost
{
    /// <summary>
    /// Runs what the command line asks for, and blocks until it ends: the work runs off the calling thread and the UI on it,
    /// so a head calls it from its <c>Main</c>.
    /// </summary>
    /// <param name="args">The process's command-line arguments.</param>
    /// <param name="initParams">The head's init params.</param>
    /// <returns>The process's exit code.</returns>
    /// <exception cref="InvalidOperationException">The entry assembly has no name, so the install has none.</exception>
    public static int Run(string[] args, DesktopInitParams initParams)
    {
        return DesktopHost.Run(args, initParams, new LinuxDesktopPlatform(initParams));
    }
}
