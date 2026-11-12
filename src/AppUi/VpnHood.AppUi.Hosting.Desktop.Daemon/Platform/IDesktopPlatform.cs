using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

/// <summary>
/// Defines what the platform says for itself: the answers every command reaches for that are not the app's, and the
/// ones only some platforms have.
/// </summary>
/// <remarks>A platform package implements it and hands it to <see cref="DesktopHost"/> with the head's init params.</remarks>
public interface IDesktopPlatform
{
    /// <summary>Gets where the install keeps things.</summary>
    IAppDesktopPaths Paths { get; }

    /// <summary>Gets the running instance, as the commands start, stop and look at it.</summary>
    IAppInstanceController Instance { get; }

    /// <summary>Gets where the service hands its API's address to an administrator.</summary>
    IDaemonChannel Channel { get; }

    /// <summary>Gets how the system tells who owns the far end of a loopback connection, checked before a call is sent.</summary>
    ILoopbackPeerCheck PeerCheck { get; }

    /// <summary>Gets the desktop session this launch runs in, as the window's single instance asks it.</summary>
    IDesktopSession DesktopSession { get; }

    /// <summary>
    /// Gets the registering and removal of the instance, where the app does it itself, as on Windows; or
    /// <see langword="null"/> where the package's installer does, as on Linux, and then those commands are not offered.
    /// </summary>
    IAppInstanceSetup? InstanceSetup { get; }

    /// <summary>Gets how the app is built for <c>daemon</c>, which the system's service manager runs, and <c>dev</c>.</summary>
    IAppDaemonHostFactory DaemonHostFactory { get; }

    /// <summary>
    /// Gets whether whoever runs this process may use the app: an administrator, by the rule the service applies to every
    /// caller of its channel.
    /// </summary>
    /// <returns><see langword="true"/> where they may.</returns>
    /// <remarks>
    /// The window and the commands ask it first, so a standard user is told at once; the service's own check stays the
    /// boundary.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The platform could not read who runs this process.</exception>
    bool IsAdministrator();

    /// <summary>
    /// Creates the console's log as the platform has it, for the process that holds the app: the journal's where standard
    /// output is the journal, as on Linux under systemd, a terminal's otherwise.
    /// </summary>
    /// <returns>The log's provider.</returns>
    ILoggerProvider CreateConsoleLoggerProvider();

    /// <summary>
    /// Creates the platform's own log, for the window, which has no console: its warnings and errors go where
    /// <c>service log</c> looks when the service gives none, the Application log on Windows and the journal on Linux.
    /// </summary>
    /// <returns>The log's provider.</returns>
    ILoggerProvider CreateSystemLogLoggerProvider();

    /// <summary>
    /// Starts the window again, as it was started, through the install's own launcher, which runs the version the install
    /// names now, as after an update replaced the service under the open window; nothing for a copy run where it lies.
    /// </summary>
    /// <param name="startHidden"><see langword="true"/> to start it in the tray.</param>
    /// <remarks>The window runs in a process of its own, which outlives this one.</remarks>
    /// <exception cref="InvalidOperationException">The launcher could not be started.</exception>
    void StartUiAgain(bool startHidden);

    /// <summary>
    /// Runs the installer's way out of the kill switch's block as an administrator, and waits for it, for
    /// <c>kill-switch off</c> when the service is not running.
    /// </summary>
    /// <param name="scriptPath">The script (<see cref="IAppDesktopPaths.KillSwitchOffScriptPath"/>).</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the script's exit code.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">The script could not be started.</exception>
    Task<int> RunKillSwitchOffScript(string scriptPath, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the service as the platform's service manager hosts it, with the time the stop leaves the app's graceful close,
    /// so the adapter is released before the manager ends the process.
    /// </summary>
    /// <param name="run">The service's run.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the process's exit code.</returns>
    /// <remarks>
    /// Windows' service control manager stops it with a call, which cancels the run, and a shutdown leaves the graceful
    /// close no time; where a signal stops it, as on Linux, <see cref="DesktopHost.HostDaemonStoppedBySignal"/> runs it.
    /// </remarks>
    Task<int> HostDaemon(DaemonRun run, CancellationToken cancellationToken);
}
