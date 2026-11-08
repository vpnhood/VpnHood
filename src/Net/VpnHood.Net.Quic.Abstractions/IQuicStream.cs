namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// Represents the QUIC controls of a server's stream, beside its ordinary <see cref="Stream"/> operations. The
/// application protocol chooses its error codes; the transport does not interpret them.
/// </summary>
public interface IQuicStream
{
    /// <summary>
    /// Gets the stream's QUIC stream ID, whose low two bits tell who opened it and whether it is bidirectional (RFC 9000,
    /// section 2.1).
    /// </summary>
    long StreamId { get; }

    /// <summary>Gets a value indicating whether the stream has an inbound direction.</summary>
    bool CanRead { get; }

    /// <summary>Gets a value indicating whether the stream has an outbound direction.</summary>
    bool CanWrite { get; }

    /// <summary>
    /// Gets a task that completes when the inbound direction closes: when the peer's FIN is read, or the direction is
    /// aborted.
    /// </summary>
    Task ReadsClosed { get; }

    /// <summary>
    /// Gets a task that completes when the outbound direction closes: when the peer acknowledges the FIN, or the
    /// direction is aborted.
    /// </summary>
    Task WritesClosed { get; }

    /// <summary>Stops receiving data and sends the peer an application error code.</summary>
    /// <param name="errorCode">The application protocol's error code.</param>
    void AbortRead(long errorCode);

    /// <summary>Stops sending data and sends the peer an application error code.</summary>
    /// <param name="errorCode">The application protocol's error code.</param>
    void AbortWrite(long errorCode);

    /// <summary>Sends FIN after the pending writes, without closing the inbound direction.</summary>
    void CompleteWrites();

    /// <summary>Writes the final bytes and FIN together.</summary>
    /// <param name="buffer">
    /// The bytes. The caller keeps them unchanged until the returned task completes, also after cancellation.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask WriteFinal(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
}
