using System.Net;

namespace VpnHood.Core.Tunneling.Connections;

/// <summary>
/// Represents a connection that carries one stream between a client and a server: a TCP connection, a QUIC stream, or
/// one logical stream of a connection that carries many in turn.
/// </summary>
/// <remarks>
/// <see cref="IAsyncDisposable.DisposeAsync"/> releases it: a reusable one ends its logical stream with the peer and
/// goes back to its owner for another request, unless <see cref="PreventReuse"/> came first, and any other closes.
/// <see cref="IDisposable.Dispose"/> is the quick close: the connection closes with no reuse, and nothing waits for its
/// peer.
/// </remarks>
public interface IStreamConnection : IDisposable, IAsyncDisposable
{
    /// <summary>Gets or sets the connection's ID, which its logs carry.</summary>
    string ConnectionId { get; set; }

    /// <summary>Gets the connection's name, which its logs carry.</summary>
    string ConnectionName { get; }

    /// <summary>Gets a value indicating whether this is the server's side of the connection, which its logs show.</summary>
    bool IsServer { get; }

    /// <summary>Gets a value indicating whether the connection is up.</summary>
    bool Connected { get; }

    /// <summary>Gets the stream the connection carries.</summary>
    Stream Stream { get; }

    /// <summary>Gets the connection's local endpoint.</summary>
    IPEndPoint LocalEndPoint { get; }

    /// <summary>Gets the peer's endpoint.</summary>
    IPEndPoint RemoteEndPoint { get; }

    /// <summary>
    /// Gets or sets a value indicating whether an HTTP response to the connection's request header is still due before
    /// the request's own answer: the server writes it, and the client reads it.
    /// </summary>
    bool RequireHttpResponse { get; set; }

    /// <summary>Makes the connection's release close it rather than keep it for another request.</summary>
    /// <remarks>A decorator passes it on, so it reaches the reusable connection however that is wrapped.</remarks>
    void PreventReuse();
}