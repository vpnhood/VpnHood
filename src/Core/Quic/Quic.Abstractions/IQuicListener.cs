using System.Net;

namespace VpnHood.Core.Quic.Abstractions;

/// <summary>
/// A listening QUIC endpoint that accepts inbound connections (server side).
/// </summary>
public interface IQuicListener : IAsyncDisposable
{
    IPEndPoint LocalEndPoint { get; }
    ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken);
}
