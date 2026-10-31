using System.Net;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Server.Listeners;
using VpnHood.Net.Http3;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class PinnedHttp3Server(IQuicListener listener, BufferedQuicServer bufferedQuic,
    CancellationTokenSource lifetime, Task run) : IAsyncDisposable
{
    public IPEndPoint EndPoint => listener.LocalEndPoint;
    public BufferedQuicServer BufferedQuic => bufferedQuic;

    public static async Task<PinnedHttp3Server> Start(X509Certificate2 certificate,
        Http3TrailerSchedule schedule, CancellationToken cancellationToken)
    {
        var bufferedQuic = new BufferedQuicServer(new MsQuicServer());
        var options = Http3ServerOptionsFactory.Create();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try {
            var listenerOptions = QuicListenerOptionsFactory.Create(
                VhUtils.GetFreeUdpEndPoint(IPAddress.Loopback), new FixedQuicCertificateSelector(certificate));
            var listener = await bufferedQuic.ListenAsync(listenerOptions, lifetime.Token);
            var run = Run(listener, options, schedule, lifetime.Token);
            return new PinnedHttp3Server(listener, bufferedQuic, lifetime, run);
        }
        catch {
            lifetime.Dispose();
            throw;
        }
    }

    private static async Task Run(IQuicListener listener, Http3ServerOptions options,
        Http3TrailerSchedule schedule, CancellationToken cancellationToken)
    {
        try {
            var connection = await listener.AcceptConnectionAsync(cancellationToken);
            if (connection is not IQuicServerConnection serverConnection) {
                await connection.DisposeAsync().AsTask().WaitAsync(options.CloseTimeout, cancellationToken);
                throw new InvalidOperationException("The controlled fixture requires a server QUIC connection.");
            }
            var streams = new AnonymousHttp3Streams();
            // Keep response composition in Core.Server; only the anonymous parser's schedule is pinned.
            var front = new Http3ServerConnection(serverConnection, options, streams, streams,
                new Http3UnauthorizedResponder(), new PinnedHttp3TrailerScheduleSampler(schedule));
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
