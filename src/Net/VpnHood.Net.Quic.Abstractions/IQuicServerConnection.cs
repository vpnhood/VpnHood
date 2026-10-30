namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// Server-side QUIC capabilities needed by an application protocol. Streams returned by this
/// connection also implement <see cref="IQuicStream"/>; client providers need only <see cref="IQuicConnection"/>.
/// </summary>
public interface IQuicServerConnection : IQuicConnection
{
    /// <summary>The TLS server name sent by the client, or an empty string when absent.</summary>
    string TargetHostName { get; }

    /// <summary>Opens an outbound, write-only stream, waiting for the peer's stream allowance.</summary>
    ValueTask<Stream> OpenOutboundUnidirectionalStreamAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection with the application protocol's error code.</summary>
    ValueTask CloseAsync(long errorCode, CancellationToken cancellationToken);
}
