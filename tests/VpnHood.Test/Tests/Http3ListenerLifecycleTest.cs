using System.Net;
using VpnHood.Core.Server;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3ListenerLifecycleTest : TestBase
{
    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task A_stalled_final_GOAWAY_write_is_cancelled_only_after_native_connection_close_starts()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        var quicServer = new FailingQuicServer(blockFinalGoAway: true);
        await using var server = new VpnHoodServer(accessManager, new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            QuicServer = quicServer,
            StoragePath = TestHelper.WorkingPath,
            AutoDisposeAccessManager = false,
            PublicIpDiscovery = false
        });
        var configuration = new ServerHostConfiguration {
            TcpEndPoints = [TestWebServerLocalEps.AllocateFreeTcpEndPoint(IPAddress.Loopback)],
            UdpEndPoints = [],
            QuicEndPoints = [IPEndPoint.Parse("127.0.0.1:443")],
            DnsServers = null,
            Certificates = [accessManager.DefaultCert],
            UdpChannelBufferSize = null
        };

        try {
            var statuses = await server.ServerHost.Configure(configuration, TestCt);
            foreach (var status in statuses)
                Assert.IsNull(status.Error, $"{status.Protocol} {status.EndPoint}: {status.Error?.Message}");
            var listener = quicServer.Listeners.Single();
            var connection = listener.Connection;
            var control = connection.OutboundControl;
            await control.FinalWriteStarted.WaitAsync(TimeSpan.FromSeconds(5), TestCt);

            // SETTINGS succeeds, but the peer never acknowledges the final GOAWAY write. Its
            // five-second deadline must start native close before cancellation can reset this stream.
            long closeCodeAtCancellation;
            try {
                closeCodeAtCancellation = await control.FinalWriteCancelled.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
            }
            catch (TimeoutException) {
                TestContext.WriteLine($"Stalled GOAWAY: native close={control.NativeCloseCode}; " +
                                      $"cancellation={control.FinalWriteCancelled.Status}; " +
                                      $"write ended={control.FinalWriteEnded.Status}; stream disposals={control.DisposeCount}; " +
                                      $"connection disposal={connection.DisposeStarted.Status}.");
                throw;
            }
            Assert.AreEqual(0x100L, closeCodeAtCancellation,
                "Canceling a critical-stream write before native close can expose the wrong wire error.");
            await connection.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            connection.ReleaseDispose();
            await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            Assert.AreEqual(1, control.DisposeCount);
            Assert.AreEqual(1, connection.DisposeCount);
            Assert.AreEqual(1, listener.DisposeCount);
            Assert.IsEmpty(server.ServerHost.QuicEndPoints);
            Assert.AreEqual(ServerState.Disposed, server.State);
        }
        finally {
            quicServer.ReleaseConnections();
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task An_ended_QUIC_listener_is_removed_from_live_endpoints_and_restarted_by_configuration()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        var quicServer = new FailingQuicServer();
        await using var server = new VpnHoodServer(accessManager, new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            QuicServer = quicServer,
            StoragePath = TestHelper.WorkingPath,
            AutoDisposeAccessManager = false,
            PublicIpDiscovery = false
        });
        var endPoint = IPEndPoint.Parse("127.0.0.1:443");
        var configuration = new ServerHostConfiguration {
            TcpEndPoints = [TestWebServerLocalEps.AllocateFreeTcpEndPoint(IPAddress.Loopback)],
            UdpEndPoints = [],
            QuicEndPoints = [endPoint],
            DnsServers = null,
            Certificates = [accessManager.DefaultCert],
            UdpChannelBufferSize = null
        };

        try {
            var statuses = await server.ServerHost.Configure(configuration, TestCt);
            foreach (var status in statuses)
                Assert.IsNull(status.Error, $"{status.Protocol} {status.EndPoint}: {status.Error?.Message}");
            var firstListener = quicServer.Listeners.Single();
            await firstListener.Connection.AcceptStarted.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            Assert.AreEqual(endPoint, server.ServerHost.QuicEndPoints.Single());

            // The accept loop dies while its connection is still open. The port disappears and the
            // front is cancelled immediately, before another configuration applies any changes.
            firstListener.EndAccept();
            await AssertEqualsWait(0, () => server.ServerHost.QuicEndPoints.Count,
                "An ended QUIC accept loop must not remain advertised as a working endpoint.");
            await firstListener.Connection.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            var reconfigure = server.ServerHost.Configure(configuration, TestCt);
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestCt);
            Assert.IsFalse(reconfigure.IsCompleted, "Reconfiguration must wait for the old connection front to close.");
            firstListener.Connection.ReleaseDispose();

            statuses = await reconfigure.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            foreach (var status in statuses)
                Assert.IsNull(status.Error, $"{status.Protocol} {status.EndPoint}: {status.Error?.Message}");
            Assert.HasCount(2, quicServer.Listeners);
            Assert.AreEqual(1, firstListener.DisposeCount);
            Assert.AreEqual(1, firstListener.Connection.DisposeCount);
            var secondListener = quicServer.Listeners[1];
            await secondListener.Connection.AcceptStarted.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            Assert.AreEqual(endPoint, server.ServerHost.QuicEndPoints.Single());

            // A healthy listener survives repeated configuration. Shutdown must also wait for its
            // connection front, including an outbound control-stream open held by zero peer credit.
            await server.ServerHost.Configure(configuration, TestCt);
            Assert.HasCount(2, quicServer.Listeners);
            var close = server.DisposeAsync().AsTask();
            await secondListener.Connection.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestCt);
            Assert.IsFalse(close.IsCompleted, "Server shutdown must wait for the current connection front to close.");
            secondListener.Connection.ReleaseDispose();
            await close.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
            Assert.IsEmpty(server.ServerHost.QuicEndPoints);
            Assert.AreEqual(1, firstListener.DisposeCount);
            Assert.AreEqual(1, secondListener.DisposeCount);
            Assert.AreEqual(1, secondListener.Connection.DisposeCount);
            Assert.AreEqual(ServerState.Disposed, server.State);
        }
        finally {
            // Let await-using finish even when an assertion fails while a native close is held.
            quicServer.ReleaseConnections();
        }
    }

    private sealed class FailingQuicServer(bool blockFinalGoAway = false) : IQuicServer
    {
        public List<FailingQuicListener> Listeners { get; } = [];

        public void ReleaseConnections()
        {
            foreach (var listener in Listeners)
                listener.Connection.ReleaseDispose();
        }

        public ValueTask<IQuicListener> ListenAsync(QuicListenerOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var listener = new FailingQuicListener(options.ListenEndPoint, blockFinalGoAway);
            Listeners.Add(listener);
            return ValueTask.FromResult<IQuicListener>(listener);
        }
    }

    private sealed class FailingQuicListener(IPEndPoint endPoint, bool blockFinalGoAway) : IQuicListener
    {
        private readonly TaskCompletionSource _acceptEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _connectionAccepted;
        private int _disposeCount;

        public IPEndPoint LocalEndPoint => endPoint;
        public ClosingQuicConnection Connection { get; } = new(endPoint, blockFinalGoAway);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void EndAccept() => _acceptEnded.TrySetResult();

        public async ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _connectionAccepted, 1) == 0)
                return Connection;
            await _acceptEnded.Task.WaitAsync(cancellationToken);
            throw new ObjectDisposedException(nameof(FailingQuicListener));
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            _acceptEnded.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ClosingQuicConnection(IPEndPoint localEndPoint, bool blockFinalGoAway) : IQuicServerConnection
    {
        private readonly TaskCompletionSource _acceptStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposeReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<Stream> _noStreams = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _controlAccepted;
        private int _disposeCount;

        public IPEndPoint LocalEndPoint => localEndPoint;
        public IPEndPoint RemoteEndPoint { get; } = IPEndPoint.Parse("127.0.0.1:12345");
        public Task AcceptStarted => _acceptStarted.Task;
        public Task DisposeStarted => _disposeStarted.Task;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public GoAwayControlStream OutboundControl { get; } = new(outbound: true);

        public void ReleaseDispose() => _disposeReleased.TrySetResult();

        public ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<Stream> OpenOutboundUnidirectionalStreamAsync(CancellationToken cancellationToken) =>
            blockFinalGoAway
                ? ValueTask.FromResult<Stream>(OutboundControl)
                : new ValueTask<Stream>(_noStreams.Task.WaitAsync(cancellationToken));

        public async ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken)
        {
            _acceptStarted.TrySetResult();
            if (blockFinalGoAway && Interlocked.Exchange(ref _controlAccepted, 1) == 0)
                return new GoAwayControlStream(outbound: false);
            return await _noStreams.Task.WaitAsync(cancellationToken);
        }

        public ValueTask CloseAsync(long errorCode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OutboundControl.RecordNativeClose(errorCode);
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            _disposeStarted.TrySetResult();
            await _disposeReleased.Task;
        }
    }

    private sealed class GoAwayControlStream(bool outbound)
        : MemoryStream(outbound ? [] : new byte[] { 0, 4, 0, 7, 1, 0 }), IQuicStream
    {
        private readonly TaskCompletionSource _finalWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<long> _finalWriteCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finalWriteEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _neverClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _nativeCloseCode;
        private int _writeCount;
        private int _disposeCount;

        public long StreamId => outbound ? 3 : 2;
        public override bool CanRead => !outbound;
        public override bool CanWrite => outbound;
        public Task ReadsClosed => _neverClosed.Task;
        public Task WritesClosed => _neverClosed.Task;
        public Task FinalWriteStarted => _finalWriteStarted.Task;
        public Task<long> FinalWriteCancelled => _finalWriteCancelled.Task;
        public Task FinalWriteEnded => _finalWriteEnded.Task;
        public long NativeCloseCode => Volatile.Read(ref _nativeCloseCode);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public void RecordNativeClose(long errorCode) => Volatile.Write(ref _nativeCloseCode, errorCode);
        public void AbortRead(long errorCode) { }
        public void AbortWrite(long errorCode) { }
        public void CompleteWrites() { }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Position < Length
                ? base.ReadAsync(buffer, cancellationToken)
                : new ValueTask<int>(_neverClosed.Task.WaitAsync(cancellationToken));

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _writeCount) == 1)
                return; // Initial SETTINGS completes before the client GOAWAY is read.
            Assert.AreEqual((byte)7, buffer.Span[0], "The blocked write must be the final GOAWAY frame.");
            using var registration = cancellationToken.Register(() =>
                _finalWriteCancelled.TrySetResult(Volatile.Read(ref _nativeCloseCode)));
            _finalWriteStarted.TrySetResult();
            try {
                // This observer also releases the write. A second WaitAsync(token) registration can
                // resume first during cancellation and remove this observer before it records ordering.
                await _finalWriteCancelled.Task;
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally {
                _finalWriteEnded.TrySetResult();
            }
        }

        public override ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return base.DisposeAsync();
        }
    }
}
