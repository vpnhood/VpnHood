namespace VpnHood.Core.Quic.Abstractions;

/// <summary>
/// Server-side QUIC listener factory. Creates listeners bound to the requested endpoint.
/// </summary>
public interface IQuicServer
{
    ValueTask<IQuicListener> ListenAsync(QuicListenerOptions options, CancellationToken cancellationToken);
}
