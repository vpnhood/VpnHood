using System.Net;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Server.Listeners;
using VpnHood.Net.Http3;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class GatedHttp3Server(IQuicListener listener, CancellationTokenSource lifetime, Task run) : IAsyncDisposable
{
    public IPEndPoint EndPoint => listener.LocalEndPoint;

    public static async Task<GatedHttp3Server> Start(X509Certificate2 certificate,
        int byteCount, int headerByteCount, bool finish, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(headerByteCount);
        if (headerByteCount >= byteCount)
            throw new ArgumentOutOfRangeException(nameof(headerByteCount));
        var options = Http3ServerOptionsFactory.Create();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var gate = new Http3RequestGate();
        var bufferedQuic = new BufferedQuicServer(new MsQuicServer(), gate, byteCount, headerByteCount, finish);
        try {
            var listenerOptions = QuicListenerOptionsFactory.Create(
                VhUtils.GetFreeUdpEndPoint(IPAddress.Loopback), new FixedQuicCertificateSelector(certificate));
            var listener = await bufferedQuic.ListenAsync(listenerOptions, lifetime.Token);
            var run = Run(listener, options, gate, lifetime.Token);
            return new GatedHttp3Server(listener, lifetime, run);
        }
        catch {
            lifetime.Dispose();
            throw;
        }
    }

    private static async Task Run(IQuicListener listener, Http3ServerOptions options,
        Http3RequestGate gate, CancellationToken cancellationToken)
    {
        try {
            var connection = await listener.AcceptConnectionAsync(cancellationToken);
            if (connection is not IQuicServerConnection serverConnection) {
                await connection.DisposeAsync().AsTask().WaitAsync(options.CloseTimeout, cancellationToken);
                throw new InvalidOperationException("The controlled fixture requires a server QUIC connection.");
            }
            var streams = new AnonymousHttp3Streams();
            var front = new Http3ServerConnection(serverConnection, options, streams, streams,
                new GatedHttp3Responder(gate, new Http3UnauthorizedResponder()));
            await front.Run(cancellationToken);
        }
        catch (OperationCanceledException) {
            if (!cancellationToken.IsCancellationRequested)
                throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try {
            await lifetime.CancelAsync();
            var closeListener = listener.DisposeAsync().AsTask();
            // The engine joins its request readers and bounded native closes before the fixture releases them.
            await Task.WhenAll(closeListener, run).WaitAsync(Http3Probe.Timeout + Http3Probe.Timeout);
        }
        finally {
            lifetime.Dispose();
        }
    }
}
