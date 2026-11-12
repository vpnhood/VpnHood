namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

/// <summary>
/// Defines the registering of the instance with the system and its removal, where the app does that itself rather than
/// its package's installer, as on Windows.
/// </summary>
/// <remarks>
/// Both print for a person and return only an exit code, and both ask for elevation where this process has none, but
/// for an install folder that is not safe, which is refused before.
/// </remarks>
public interface IAppInstanceSetup
{
    /// <summary>Registers the instance; on a registered one, it brings the registration back to what it should be.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the exit code.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The service control manager refused.</exception>
    /// <exception cref="InvalidOperationException">
    /// Elevation could not be asked for or was refused, or a registry key the setup needs is not available.
    /// </exception>
    Task<int> Install(CancellationToken cancellationToken);

    /// <summary>Removes the instance.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the exit code.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="SystemException">The service did not stop in time, as <c>System.ServiceProcess.TimeoutException</c>.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The service control manager refused.</exception>
    /// <exception cref="InvalidOperationException">
    /// Elevation could not be asked for or was refused, or a registry key the setup needs is not available.
    /// </exception>
    Task<int> Uninstall(CancellationToken cancellationToken);
}
