using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

/// <summary>
/// Defines where an install keeps things, as the commands need to know it: the window's share and the service's.
/// </summary>
/// <remarks>Nothing here is a constant or a static: a command is handed the one for the machine it runs on.</remarks>
public interface IAppDesktopPaths : IDesktopUiPaths
{
    /// <summary>
    /// Gets the word a person types to run this again, which every hint prints, as <c>vhclient</c>; the binary's name where
    /// the install names none.
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Gets a value indicating whether this process was started by that word rather than by the window's own executable,
    /// as a desktop entry runs it: run alone, the one prints its help and the other opens the window.
    /// </summary>
    bool IsStartedAsCommand { get; }

    /// <summary>Gets the service's storage: the settings, the profiles and the log, which only it and administrators may open.</summary>
    /// <exception cref="InvalidOperationException">The platform cannot name the folder.</exception>
    string StoragePath { get; }

    /// <summary>
    /// Gets the app's storage where a debugger runs it in the person's own process (<c>dev</c>), which may not write the
    /// service's. The default is the UI's folder; a platform whose UI folder is a cache keeps it elsewhere.
    /// </summary>
    string DevStoragePath => UiDataPath;

    /// <summary>
    /// Gets the installer's script that lifts the kill switch's block with neither the service nor the app, which the
    /// command line runs when the service is not running; or <see langword="null"/> outside an install.
    /// </summary>
    /// <exception cref="InvalidOperationException">The platform cannot name the install's folder.</exception>
    string? KillSwitchOffScriptPath { get; }
}
