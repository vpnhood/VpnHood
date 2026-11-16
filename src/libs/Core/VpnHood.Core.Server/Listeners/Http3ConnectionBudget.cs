using Microsoft.Extensions.Logging;
using System.Net;
using VpnHood.Core.Server.ProbeResistance;
using VpnHood.Net.Http3;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Server.Listeners;

internal sealed class Http3ConnectionBudget(IQuicServerConnection transport, UnauthenticatedConnections connections,
    TimeSpan closeTimeout)
    : IQuicServerConnection, IDisposable
{
    private bool _released;
    private bool _evicted;

    public IPEndPoint LocalEndPoint => transport.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => transport.RemoteEndPoint;

    public void Register()
    {
        // A connection identity keeps transports sharing one endpoint tuple independent.
        connections.Add(new IPEndPointPair(LocalEndPoint, RemoteEndPoint), this, this);
    }

    public void Release()
    {
        if (!Interlocked.Exchange(ref _released, true))
            connections.Remove(this, this);
    }

    public ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken) =>
        transport.OpenOutboundStreamAsync(cancellationToken);

    public ValueTask<Stream> OpenOutboundUnidirectionalStreamAsync(CancellationToken cancellationToken) =>
        transport.OpenOutboundUnidirectionalStreamAsync(cancellationToken);

    public ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken) =>
        transport.AcceptInboundStreamAsync(cancellationToken);

    public ValueTask CloseAsync(long errorCode, CancellationToken cancellationToken)
    {
        // Closing transports leave the budget before waiting for the peer or stream cleanup.
        Release();
        return transport.CloseAsync(errorCode, cancellationToken);
    }

    private async Task CloseEvicted(CancellationToken cancellationToken)
    {
        try {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(closeTimeout);
            await transport.CloseAsync(Http3ErrorCode.ExcessiveLoad, timeout.Token).Vhc();
        }
        catch (OperationCanceledException ex) {
            VhLogger.Instance.LogDebug(ex, "Closing an evicted QUIC connection was cancelled.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Error closing an evicted QUIC connection.");
        }
    }

    // Eviction starts native close without waiting; the listener still owns the engine's lifetime.
    public void Dispose()
    {
        Release();
        if (!Interlocked.Exchange(ref _evicted, true))
            _ = CloseEvicted(CancellationToken.None);
    }

    public ValueTask DisposeAsync()
    {
        Release();
        return transport.DisposeAsync();
    }
}
