using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Sockets;
using VpnHood.Core.Server.Listeners.Http3;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3TunnelLifetimeTest : TestBase
{
    [TestMethod]
    [TestCategory("Http3Slow")]
    [Timeout(210_000, CooperativeCancellation = true)]
    public async Task An_active_QUIC_tunnel_outlives_the_HTTP3_idle_timeout_and_reuses_its_connection()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            Assert.Inconclusive("QUIC is not supported on this platform.");

        var adapter = TestHelper.CreateTestVpnAdapter();
        var accessOptions = TestHelper.CreateFileAccessManagerOptions();
        accessOptions.SessionOptions.ChannelIdleTimeout = TimeSpan.FromMinutes(5);
        accessOptions.SessionOptions.MinClientProtocolVersion = 15;
        using var accessManager = TestHelper.CreateAccessManager(accessOptions);
        await using var server = await TestHelper.CreateServer(accessManager,
            socketFactory: TestHelper.CreateTestSocketFactory(adapter));

        var token = TestHelper.CreateAccessToken(server);
        var clientOptions = TestHelper.CreateClientOptions(token, ChannelProtocol.Quic);
        clientOptions.MinPacketChannelTimespan = TimeSpan.FromMinutes(5);
        clientOptions.MaxPacketChannelTimespan = TimeSpan.FromMinutes(5);
        var sockets = new TrackingSocketFactory();
        await using var client = await TestHelper.CreateClient(clientOptions, vpnAdapter: adapter,
            socketFactory: sockets);
        using var udpClient = new UdpClient(AddressFamily.InterNetwork);

        await TestHelper.Test_UdpEcho(udpClient).WaitAsync(TestCt);
        Assert.AreEqual(ChannelProtocol.Quic, client.ChannelProtocol);
        Assert.AreEqual(1, sockets.ConnectionCount);
        Assert.AreEqual(1, client.RequiredSession.Status.ActivePacketChannelCount);
        var initialStreams = sockets.StreamCount;
        var initialChannels = client.RequiredSession.Status.SessionPacketChannelCount;
        Assert.IsGreaterThan(0, initialStreams);

        // Exercise the front's real idle timeout (130 seconds today), without a test override.
        // Send packets on the existing tunnel only. Opening fresh requests during this interval
        // could reset the HTTP/3 idle timer and conceal an untracked tunnel's premature closure.
        var observationTime = Http3Protocol.KeepAliveTimeout + TimeSpan.FromSeconds(5);
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < observationTime) {
            await TestHelper.Test_UdpEcho(udpClient).WaitAsync(TestCt);
            Assert.AreEqual(1, sockets.ConnectionCount, "The original QUIC connection must not be replaced.");
            Assert.AreEqual(initialStreams, sockets.StreamCount,
                "No new stream may reset the HTTP/3 idle timer during the observation.");
            Assert.AreEqual(initialChannels, client.RequiredSession.Status.SessionPacketChannelCount,
                "The packet channel must remain the original authenticated stream.");

            var remaining = observationTime - elapsed.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5), TestCt);
        }

        // Traffic after the entire idle window proves the original stream still works. A new proxy
        // request then proves the same QUIC connection still accepts and authenticates new streams.
        await TestHelper.Test_UdpEcho(udpClient).WaitAsync(TestCt);
        Assert.AreEqual(initialStreams, sockets.StreamCount);
        Assert.AreEqual(initialChannels, client.RequiredSession.Status.SessionPacketChannelCount);
        await TestHelper.Test_Https().WaitAsync(TestCt);
        Assert.IsGreaterThan(initialStreams, sockets.StreamCount);
        Assert.AreEqual(1, sockets.ConnectionCount);
        Assert.AreEqual(ClientState.Connected, client.State);

        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15), TestCt);
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15), TestCt);
        Assert.AreEqual(sockets.ConnectionCount, sockets.DisposedConnectionCount);
        Assert.AreEqual(ClientState.Disposed, client.State);
        Assert.AreEqual(ServerState.Disposed, server.State);
    }

    // Observe the real transport underneath the client pool so a successful automatic reconnect
    // cannot make the lifetime assertion pass. All network behavior still comes from MsQuic.
    private sealed class TrackingSocketFactory : TestSocketFactory
    {
        private int _connectionCount;
        private int _streamCount;
        private int _disposedConnectionCount;
        public int ConnectionCount => Volatile.Read(ref _connectionCount);
        public int StreamCount => Volatile.Read(ref _streamCount);
        public int DisposedConnectionCount => Volatile.Read(ref _disposedConnectionCount);

        public override IQuicClient CreateQuicClient() => new TrackingClient(base.CreateQuicClient(), this);

        private sealed class TrackingClient(IQuicClient client, TrackingSocketFactory owner) : IQuicClient
        {
            public async ValueTask<IQuicConnection> ConnectAsync(QuicClientConnectOptions options,
                CancellationToken cancellationToken)
            {
                var connection = await client.ConnectAsync(options, cancellationToken);
                Interlocked.Increment(ref owner._connectionCount);
                return new TrackingConnection(connection, owner);
            }
        }

        private sealed class TrackingConnection(IQuicConnection connection, TrackingSocketFactory owner)
            : IQuicConnection
        {
            private bool _disposed;
            public IPEndPoint LocalEndPoint => connection.LocalEndPoint;
            public IPEndPoint RemoteEndPoint => connection.RemoteEndPoint;

            public async ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken)
            {
                var stream = await connection.OpenOutboundStreamAsync(cancellationToken);
                Interlocked.Increment(ref owner._streamCount);
                return stream;
            }

            public ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken) =>
                connection.AcceptInboundStreamAsync(cancellationToken);

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, true))
                    return;
                await connection.DisposeAsync();
                Interlocked.Increment(ref owner._disposedConnectionCount);
            }
        }
    }
}
