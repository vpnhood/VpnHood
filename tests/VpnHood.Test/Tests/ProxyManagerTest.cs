using VpnHood.Net.Toolkit.Net;
using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Packets;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Proxies;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Test.Providers;
using VpnHood.Core.Common.Configuration;

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

    private static ProxyManager CreateProxyManager(bool isPingSupported = false)
    {
        return new ProxyManager(new BindingSocketFactory(new TestSocketFactory()), new ProxyManagerOptions {
            IsPingSupported = isPingSupported,
            PacketProxyCallbacks = null,
            UdpTimeout = TransportDefaults.UdpTimeout,
            IcmpTimeout = TransportDefaults.IcmpTimeout,
            MaxUdpClientCount = TransportDefaults.MaxUdpClientCount,
            MaxPingClientCount = TransportDefaults.MaxPingClientCount,
            PacketQueueCapacity = TransportDefaults.ProxyPacketQueueCapacity,
            UdpBufferSize = null,
            LogScope = null
        });
    }

    // the packet's copy over a memory that records its disposal
    private static TestMemoryOwner CopyToTestMemory(IpPacket ipPacket)
    {
        using (ipPacket)
            return new TestMemoryOwner(ipPacket.Buffer.ToArray());
    }

    [TestMethod]
    public async Task Udp_proxy_disposes_each_packet_it_sent()
    {
        using var proxyManager = CreateProxyManager();
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        // the payload goes out on the proxy's socket, and the packet to no one
        var memory = CopyToTestMemory(PacketBuilder.BuildUdp(IPEndPoint.Parse("10.0.0.2:2000"),
            peer.Client.GetLocalEndPoint(), new byte[16]));
        var ipPacket = PacketBuilder.Attach(memory);
        proxyManager.SendPacketQueued(ipPacket);
        await peer.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await WaitFor(() => memory.IsDisposed, "The UDP proxy should dispose the packet it sent.");
        GC.KeepAlive(ipPacket);
    }

    [TestMethod]
    public async Task Ping_proxy_disposes_each_request_it_sent()
    {
        using var proxyManager = CreateProxyManager(isPingSupported: true);
        var replied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        proxyManager.PacketReceived += (_, reply) => {
            reply.Dispose();
            replied.TrySetResult();
        };

        // the request goes out as a ping, and the request to no one; the reply shows it went. No payload: Linux
        // refuses one to a process without raw sockets
        var memory = CopyToTestMemory(PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
            IPAddress.Loopback, []));
        var request = PacketBuilder.Attach(memory);
        proxyManager.SendPacketQueued(request);
        await replied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitFor(() => memory.IsDisposed, "The ping proxy should dispose the request it sent.");
        GC.KeepAlive(request);
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

    [TestMethod]
    public async Task Closing_channel_ends_its_wait_on_the_speed_limit()
    {
        // the host's bytes wait on a limit that pays them off in about two seconds
        using var trafficMeter = new TrafficMeter {
            MaxSpeed = new Traffic(sent: 1000, received: 0),
            MaxSpeedBurst = new Traffic(sent: 0, received: 0)
        };
        var hostIn = new System.IO.Pipelines.Pipe();
        var hostOut = new System.IO.Pipelines.Pipe();
        var tunnelIn = new System.IO.Pipelines.Pipe();
        var tunnelOut = new System.IO.Pipelines.Pipe();
        using var channel = new ProxyChannel("throttled-channel",
            new TestStreamConnection(new DuplexStream(hostIn.Reader.AsStream(), hostOut.Writer.AsStream())),
            new TestStreamConnection(new DuplexStream(tunnelIn.Reader.AsStream(), tunnelOut.Writer.AsStream())),
            new TransferBufferSize(4096, 4096), trafficMeter);
        channel.Start();
        await hostIn.Writer.WriteAsync(new byte[2048]);
        await Task.Delay(200); // the host's pump has read them and waits on the limit

        // the tunnel's side ends: the channel closes at once, not once the wait is over
        await tunnelIn.Writer.CompleteAsync();
        await WaitFor(() => channel.State == PacketChannelState.Disposed,
            "The channel should close without waiting out the limit.", timeoutMs: 1000);
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
