namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

/// <summary>Defines the channel as the service holds it open.</summary>
public interface IDaemonChannelListener : IAsyncDisposable
{
    /// <summary>Waits for the next caller.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the caller.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IDaemonChannelCaller> Accept(CancellationToken cancellationToken);
}
