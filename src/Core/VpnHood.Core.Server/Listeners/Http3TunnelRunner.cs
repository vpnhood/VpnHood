using VpnHood.Core.Tunneling.Connections;
using VpnHood.Net.Http3;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.Listeners;

internal sealed class Http3TunnelRunner(Http3ConnectionBudget budget,
    Func<IStreamConnection, CancellationToken, Task> processNewConnection) : IHttp3StreamRunner
{
    public async Task Run(Http3StreamContext context, Stream stream, CancellationToken cancellationToken)
    {
        budget.Release();
        await using var tunnel = new QuicStreamConnection(stream, context.LocalEndPoint, context.RemoteEndPoint,
            "tunnel", isServer: true);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tunnel.Disposed += (_, _) => closed.TrySetResult();
        await processNewConnection(tunnel, cancellationToken).Vhc();
        // A proxy or reuse pool can inherit the stream after admission; its owner ends this request.
        await closed.Task.WaitAsync(cancellationToken).Vhc();
    }
}
