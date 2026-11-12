namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

/// <summary>
/// Defines where the service hands its API's address and token, to an administrator alone: a named pipe on Windows, a
/// Unix socket on Linux, each telling who calls.
/// </summary>
public interface IDaemonChannel
{
    /// <summary>Opens the service's side, which takes callers until disposed.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the listener.</returns>
    Task<IDaemonChannelListener> Listen(CancellationToken cancellationToken);

    /// <summary>Opens a client's side.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the connection's stream.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">On Linux, the channel's socket could not be reached.</exception>
    /// <exception cref="TimeoutException">On Windows, the channel did not answer in time.</exception>
    /// <exception cref="InvalidOperationException">
    /// On Windows, the service has not opened its channel, or someone not trusted serves it.
    /// </exception>
    Task<Stream> Connect(CancellationToken cancellationToken);
}
