using System.IO.Pipes;

namespace VpnHood.AppUi.Hosting.Desktop.Platform;

/// <summary>
/// Defines the desktop session a launch runs in, as the window's single instance asks it: a person has a window in each
/// session, and a launch that finds it open lets it come forward.
/// </summary>
public interface IDesktopSession
{
    /// <summary>Gets what tells one person's sessions apart; it is empty where there is none to tell, and the person then has one window.</summary>
    string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the launch runs in Windows' sandbox, an AppContainer, which keeps the window's pipe in
    /// the app's own namespace.
    /// </summary>
    bool IsSandboxed { get; }

    /// <summary>Tries to let the process at the pipe's far end bring its window forward, where the platform supports it.</summary>
    /// <param name="window">The pipe to the running window.</param>
    void HandForegroundTo(NamedPipeClientStream window);
}
