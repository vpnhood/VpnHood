namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

/// <summary>
/// Defines the running instance, the one process on the machine that holds the app, as the commands start, stop and
/// look at it.
/// </summary>
/// <remarks>
/// It is a systemd unit on Linux and a service on Windows; each platform answers in its own terms. The <c>Show</c> calls
/// print for a person and return only an exit code.
/// </remarks>
public interface IAppInstanceController
{
    /// <summary>Gets the sentence a person reads when nothing is running: what it is, and how to start it.</summary>
    string NotRunningHint { get; }

    /// <summary>Gets whether the instance is running.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> where it runs.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">A system tool the platform runs could not be started.</exception>
    Task<bool> IsRunning(CancellationToken cancellationToken);

    /// <summary>Starts the instance.</summary>
    /// <param name="mayRegister">
    /// <see langword="true"/> to register a missing instance on the way, behind a prompt, where the app registers its
    /// instance itself, as on Windows; a start nobody asked for, as the tray's at sign-in, may not.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">On Windows, the service control manager refused.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="VpnHood.AppUi.Hosting.Desktop.Exceptions.DaemonNotRunningException">
    /// The instance is not registered, and <paramref name="mayRegister"/> is <see langword="false"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The instance could not start; the message says why.</exception>
    /// <exception cref="TimeoutException">The instance did not start in time.</exception>
    Task Start(bool mayRegister, CancellationToken cancellationToken);

    /// <summary>Stops the instance.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the exit code.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">On Windows, the service did not stop in time.</exception>
    /// <exception cref="InvalidOperationException">A system tool the platform runs could not be started.</exception>
    Task<int> Stop(CancellationToken cancellationToken);

    /// <summary>Restarts the instance.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the exit code.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">On Windows, the service control manager refused.</exception>
    /// <exception cref="TimeoutException">On Windows, the service did not stop or start in time.</exception>
    /// <exception cref="InvalidOperationException">
    /// A system tool could not be started, or the instance could not be registered or started again.
    /// </exception>
    Task<int> Restart(CancellationToken cancellationToken);

    /// <summary>Prints the instance's status for a person.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the exit code.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">A system tool the platform runs could not be started.</exception>
    Task<int> ShowStatus(CancellationToken cancellationToken);

    /// <summary>
    /// Prints what the platform keeps of the log itself, for when the service gives none, as for a start that failed before
    /// the app opened its log.
    /// </summary>
    /// <param name="follow">
    /// <see langword="true"/> to follow the log as it grows, where the platform can; Windows prints what it has.
    /// </param>
    /// <param name="lines">The number of the last lines to print.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the exit code.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">A system tool the platform runs could not be started.</exception>
    Task<int> ShowOfflineLog(bool follow, int lines, CancellationToken cancellationToken);
}
