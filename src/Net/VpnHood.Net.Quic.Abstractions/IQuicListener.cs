using System.Net;

namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// A listening QUIC endpoint that accepts inbound connections (server side).
/// </summary>
public interface IQuicListener : IAsyncDisposable
{
    /// <summary>Gets the address and port the listener is bound to, with the port the system chose when 0 was asked.</summary>
    IPEndPoint LocalEndPoint { get; }

    /// <summary>
    /// Waits for the next inbound connection whose TLS handshake has completed.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. Its result is the connection; dispose it to close it.</returns>
    ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken);
}
