using System.Net;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Proxies;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// deliberately not TestBase: these tests need no client/server harness, and TestBase's
// mock web server binds test IPs that are unavailable on non-Windows dev machines
[TestClass]
public class ProxyManagerTest
{
    private static async Task WaitFor(Func<bool> condition, string message, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition()) {
            if (Environment.TickCount64 > deadline)
                Assert.Fail(message);
            await Task.Delay(20);
        }
    }

    private static ProxyManager CreateProxyManager()
    {
        return new ProxyManager(new TestSocketFactory(), new ProxyManagerOptions {
            IsPingSupported = false,
            PacketProxyCallbacks = null,
            UdpTimeout = TunnelDefaults.UdpTimeout,
            IcmpTimeout = TunnelDefaults.IcmpTimeout,
            MaxUdpClientCount = TunnelDefaults.MaxUdpClientCount,
            MaxPingClientCount = TunnelDefaults.MaxPingClientCount,
            PacketQueueCapacity = TunnelDefaults.ProxyPacketQueueCapacity,
            UdpBufferSize = null,
            LogScope = null,
            UseUdpProxy2 = true,
            AutoDisposePackets = true
        });
    }

    [TestMethod]
    public async Task Disposed_channels_must_be_swept_from_tracking_list()
    {
        using var proxyManager = CreateProxyManager();

        // A channel over two in-memory streams: the empty side hits EOF immediately, so the
        // channel pumps finish and the channel disposes ITSELF — exactly like a real bypassed
        // flow whose peer closed. It must not stay in the manager's tracking list afterward.
        var channel = new ProxyChannel("test-channel",
            new TestStreamConnection(new MemoryStream(new byte[1000])), // host: 1000 bytes then EOF
            new TestStreamConnection(new MemoryStream()), // tunnel: EOF immediately
            new TransferBufferSize(4096, 4096));

        proxyManager.AddChannel(channel, disposeOnFail: true);
        Assert.AreEqual(1, proxyManager.TcpConnectionCount);

        // the channel disposes itself when its pumps finish
        await WaitFor(() => channel.State == PacketChannelState.Disposed,
            "the channel over EOF streams must dispose itself");

        // traffic recorded so far must survive the sweep (monotonic counters)
        var trafficBefore = proxyManager.Traffic;

        proxyManager.CleanupChannels();
        Assert.AreEqual(0, proxyManager.TcpConnectionCount,
            "a disposed channel must be removed from the tracking list");
        Assert.AreEqual(trafficBefore.Sent, proxyManager.Traffic.Sent);
        Assert.AreEqual(trafficBefore.Received, proxyManager.Traffic.Received);
    }

    [TestMethod]
    public async Task Live_channels_must_not_be_swept()
    {
        using var proxyManager = CreateProxyManager();

        // Never-completing pipe-backed streams keep the channel alive (Connected).
        var hostToTunnel = new System.IO.Pipelines.Pipe();
        var tunnelToHost = new System.IO.Pipelines.Pipe();
        var channel = new ProxyChannel("live-channel",
            new TestStreamConnection(new DuplexStream(hostToTunnel.Reader.AsStream(), tunnelToHost.Writer.AsStream())),
            new TestStreamConnection(new DuplexStream(tunnelToHost.Reader.AsStream(), hostToTunnel.Writer.AsStream())),
            new TransferBufferSize(4096, 4096));

        proxyManager.AddChannel(channel, disposeOnFail: true);
        await Task.Delay(100); // let the pumps start

        proxyManager.CleanupChannels();
        Assert.AreEqual(1, proxyManager.TcpConnectionCount, "a live channel must stay in the tracking list");

        channel.Dispose();
        proxyManager.CleanupChannels();
        Assert.AreEqual(0, proxyManager.TcpConnectionCount);
    }

    private sealed class TestStreamConnection(Stream stream) : IStreamConnection
    {
        public string ConnectionId { get; set; } = Guid.NewGuid().ToString();
        public string ConnectionName => "test";
        public bool IsServer => false;
        public bool Connected => stream is { CanRead: true, CanWrite: true };
        public Stream Stream => stream;
        public IPEndPoint LocalEndPoint { get; } = new(IPAddress.Loopback, 1);
        public IPEndPoint RemoteEndPoint { get; } = new(IPAddress.Loopback, 2);
        public bool RequireHttpResponse { get; set; }
        public void Dispose() => stream.Dispose();
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }

    private sealed class DuplexStream(Stream readStream, Stream writeStream) : Stream
    {
        public override bool CanRead => readStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => writeStream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            readStream.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            readStream.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) =>
            writeStream.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            writeStream.WriteAsync(buffer, cancellationToken);

        public override void Flush() => writeStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                readStream.Dispose();
                writeStream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
