namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// QUIC controls exposed beside a server stream's ordinary <see cref="Stream"/> operations.
/// The application protocol chooses its error codes; the transport does not interpret them.
/// </summary>
public interface IQuicStream
{
    long StreamId { get; }

    /// <summary>Whether this stream has an inbound direction.</summary>
    bool CanRead { get; }

    /// <summary>Whether this stream has an outbound direction.</summary>
    bool CanWrite { get; }

    Task ReadsClosed { get; }
    Task WritesClosed { get; }

    /// <summary>Stops receiving data and sends the peer an application error code.</summary>
    void AbortRead(long errorCode);

    /// <summary>Stops sending data and sends the peer an application error code.</summary>
    void AbortWrite(long errorCode);

    /// <summary>Sends FIN after the pending writes without closing the reading side.</summary>
    void CompleteWrites();

    /// <summary>
    /// Sends the final bytes and FIN together. The caller retains the buffer until the returned operation
    /// completes, including after cancellation.
    /// </summary>
    ValueTask WriteFinal(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
}
