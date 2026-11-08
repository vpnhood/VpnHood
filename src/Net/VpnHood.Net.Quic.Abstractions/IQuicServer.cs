namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// Server-side QUIC listener factory. Creates listeners bound to the requested endpoint.
/// </summary>
public interface IQuicServer
{
    /// <summary>
    /// Creates a listener on the endpoint the options name and starts accepting connections.
    /// </summary>
    /// <param name="options">The endpoint, limits, timeouts and certificate selection.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. Its result is the listener; dispose it to stop it.</returns>
    ValueTask<IQuicListener> ListenAsync(QuicListenerOptions options, CancellationToken cancellationToken);
}
