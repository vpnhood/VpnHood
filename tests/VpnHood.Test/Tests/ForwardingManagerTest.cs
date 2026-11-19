using VpnHood.Net.Toolkit.Net;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using VpnHood.Net.Packets;
using VpnHood.Net.PacketTransports;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Forwarding;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Test.Providers;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Test.Tests;

// deliberately not TestBase: these tests need no client/server harness, and TestBase's
// mock web server binds test IPs that are unavailable on non-Windows dev machines
[TestClass]
public class ForwardingManagerTest
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

    private static ForwardingManager CreateForwardingManager(bool isPingSupported = false)
    {
        return new ForwardingManager(new BindingSocketFactory(new TestSocketFactory()), new ForwardingManagerOptions {
            IsPingSupported = isPingSupported,
            PacketForwarderCallbacks = null,
            UdpTimeout = TransportDefaults.UdpTimeout,
            IcmpTimeout = TransportDefaults.IcmpTimeout,
            MaxUdpClientCount = TransportDefaults.MaxUdpClientCount,
            MaxPingClientCount = TransportDefaults.MaxPingClientCount,
            PacketQueueCapacity = TransportDefaults.PacketForwardingQueueCapacity,
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
    public async Task Udp_forwarder_disposes_each_packet_it_sent()
    {
        using var forwardingManager = CreateForwardingManager();
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        // the payload goes out on the forwarder's socket, and the packet to no one
        var memory = CopyToTestMemory(PacketBuilder.BuildUdp(IPEndPoint.Parse("10.0.0.2:2000"),
            peer.Client.GetLocalEndPoint(), new byte[16]));
        var ipPacket = PacketBuilder.Attach(new PacketMemory(memory));
        forwardingManager.SendPacketQueued(ipPacket);
        await peer.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await WaitFor(() => memory.IsDisposed, "The UDP forwarder should dispose the packet it sent.");
    }

    [TestMethod]
    public async Task Ping_forwarder_disposes_each_request_it_sent()
    {
        using var forwardingManager = CreateForwardingManager(isPingSupported: true);
        var replied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        forwardingManager.PacketReceived += (_, reply) => {
            reply.Dispose();
            replied.TrySetResult();
        };

        // the request goes out as a ping, and the request to no one; the reply shows it went. No payload: Linux
        // refuses one to a process without raw sockets
        var memory = CopyToTestMemory(PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
            IPAddress.Loopback, []));
        var request = PacketBuilder.Attach(new PacketMemory(memory));
        forwardingManager.SendPacketQueued(request);
        await replied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitFor(() => memory.IsDisposed, "The ping forwarder should dispose the request it sent.");
    }

    [TestMethod]
    public async Task Disposed_channels_must_be_swept_from_tracking_list()
    {
        using var forwardingManager = CreateForwardingManager();

        // A channel over two in-memory streams: the empty side hits EOF immediately, so the
        // channel pumps finish and the channel disposes ITSELF — exactly like a real bypassed
        // flow whose peer closed. It must not stay in the manager's tracking list afterward.
        var channel = new StreamForwardingChannel("test-channel",
            new TestStreamConnection(new MemoryStream(new byte[1000])), // host: 1000 bytes then EOF
            new TestStreamConnection(new MemoryStream()), // tunnel: EOF immediately
            new TransferBufferSize(4096, 4096));

        forwardingManager.AddChannel(channel, disposeOnFail: true);
        Assert.AreEqual(1, forwardingManager.TcpConnectionCount);

        // the channel disposes itself when its pumps finish
        await WaitFor(() => channel.State == PacketChannelState.Disposed,
            "the channel over EOF streams must dispose itself");

        // traffic recorded so far must survive the sweep (monotonic counters)
        var trafficBefore = forwardingManager.Traffic;

        forwardingManager.CleanupChannels();
        Assert.AreEqual(0, forwardingManager.TcpConnectionCount,
            "a disposed channel must be removed from the tracking list");
        Assert.AreEqual(trafficBefore.Sent, forwardingManager.Traffic.Sent);
        Assert.AreEqual(trafficBefore.Received, forwardingManager.Traffic.Received);
    }

    [TestMethod]
    public async Task Live_channels_must_not_be_swept()
    {
        using var forwardingManager = CreateForwardingManager();

        // Never-completing pipe-backed streams keep the channel alive (Connected).
        var hostToTunnel = new System.IO.Pipelines.Pipe();
        var tunnelToHost = new System.IO.Pipelines.Pipe();
        var channel = new StreamForwardingChannel("live-channel",
            new TestStreamConnection(new DuplexStream(hostToTunnel.Reader.AsStream(), tunnelToHost.Writer.AsStream())),
            new TestStreamConnection(new DuplexStream(tunnelToHost.Reader.AsStream(), hostToTunnel.Writer.AsStream())),
            new TransferBufferSize(4096, 4096));

        forwardingManager.AddChannel(channel, disposeOnFail: true);
        await Task.Delay(100); // let the pumps start

        forwardingManager.CleanupChannels();
        Assert.AreEqual(1, forwardingManager.TcpConnectionCount, "a live channel must stay in the tracking list");

        channel.Dispose();
        forwardingManager.CleanupChannels();
        Assert.AreEqual(0, forwardingManager.TcpConnectionCount);
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
        await using var channel = new StreamForwardingChannel("throttled-channel",
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

    // a check of new destinations that refuses each one, as the server's net-scan check does once a client reaches
    // too many, and counts the times it is asked
    private sealed class RefusingForwarderCallbacks : IPacketForwarderCallbacks
    {
        private int _requestedCount;
        public int RequestedCount => Volatile.Read(ref _requestedCount);

        public void OnConnectionRequested(IpProtocol protocolType, IpEndPointValue remoteEndPoint)
        {
            Interlocked.Increment(ref _requestedCount);
            throw new UnauthorizedAccessException("Refused for the test.");
        }

        public void OnConnectionEstablished(IpProtocol protocolType, IpEndPointValue? localEndPoint,
            IpEndPointValue remoteEndPoint, bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
        }
    }

    private static void TrySend(IPacketTransport transport, IpPacket ipPacket)
    {
        try {
            transport.SendPacketQueued(ipPacket);
        }
        catch (UnauthorizedAccessException) {
            // the refusal, where the transport passes it on
        }
    }

    [TestMethod]
    public async Task A_UDP_destination_the_check_refused_is_checked_again_at_its_next_packet()
    {
        var callbacks = new RefusingForwarderCallbacks();
        using var forwardingManager = new ForwardingManager(new BindingSocketFactory(new TestSocketFactory()),
            new ForwardingManagerOptions {
                IsPingSupported = false,
                PacketForwarderCallbacks = callbacks,
                UdpTimeout = TransportDefaults.UdpTimeout,
                IcmpTimeout = TransportDefaults.IcmpTimeout,
                MaxUdpClientCount = TransportDefaults.MaxUdpClientCount,
                MaxPingClientCount = TransportDefaults.MaxPingClientCount,
                PacketQueueCapacity = TransportDefaults.PacketForwardingQueueCapacity,
                UdpBufferSize = null,
                LogScope = null
            });

        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        // The check refused the first packet, and the destination was remembered all the same: the second went out
        // unchecked
        for (var i = 0; i < 2; i++)
            TrySend(forwardingManager, PacketBuilder.BuildUdp(IPEndPoint.Parse("10.0.0.2:2000"),
                peer.Client.GetLocalEndPoint(), new byte[16]));

        await WaitFor(() => callbacks.RequestedCount == 2, "Each packet to a refused destination should be checked.");
        Assert.AreEqual(0, forwardingManager.UdpClientCount, "A refused destination should get no worker.");
    }

    [TestMethod]
    public async Task A_ping_destination_the_check_refused_is_checked_again_at_its_next_packet()
    {
        var callbacks = new RefusingForwarderCallbacks();
        using var forwardingManager = new ForwardingManager(new BindingSocketFactory(new TestSocketFactory()),
            new ForwardingManagerOptions {
                IsPingSupported = true,
                PacketForwarderCallbacks = callbacks,
                UdpTimeout = TransportDefaults.UdpTimeout,
                IcmpTimeout = TransportDefaults.IcmpTimeout,
                MaxUdpClientCount = TransportDefaults.MaxUdpClientCount,
                MaxPingClientCount = TransportDefaults.MaxPingClientCount,
                PacketQueueCapacity = TransportDefaults.PacketForwardingQueueCapacity,
                UdpBufferSize = null,
                LogScope = null
            });

        var replyCount = 0;
        forwardingManager.PacketReceived += (_, reply) => {
            reply.Dispose();
            Interlocked.Increment(ref replyCount);
        };

        // the second ping went out unchecked, and was answered
        for (var i = 0; i < 2; i++)
            TrySend(forwardingManager, PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
                IPAddress.Loopback, []));

        await WaitFor(() => callbacks.RequestedCount == 2, "Each ping to a refused destination should be checked.");
        Assert.AreEqual(0, GetPingPool(forwardingManager).ClientCount, "A refused destination should get no worker.");
        Assert.AreEqual(0, Volatile.Read(ref replyCount), "No ping should reach a refused destination.");
    }

    private static PingForwarderPool GetPingPool(ForwardingManager forwardingManager)
    {
        var field = typeof(ForwardingManager).GetField("_pingForwarderPool",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("ForwardingManager has no _pingForwarderPool.");

        return field.GetValue(forwardingManager) as PingForwarderPool
               ?? throw new InvalidOperationException("ForwardingManager holds no ping pool.");
    }

    // a check that takes each destination, counts the times it is asked, and keeps what each established notice said;
    // its notice may throw
    private sealed class RecordingForwarderCallbacks : IPacketForwarderCallbacks
    {
        private int _requestedCount;
        public int RequestedCount => Volatile.Read(ref _requestedCount);
        public bool FailEstablished { get; init; }
        public ConcurrentQueue<(IpEndPointValue? LocalEndPoint, bool IsNewLocal, bool IsNewRemote)> Established { get; } =
            new();

        public void OnConnectionRequested(IpProtocol protocolType, IpEndPointValue remoteEndPoint)
        {
            Interlocked.Increment(ref _requestedCount);
        }

        public void OnConnectionEstablished(IpProtocol protocolType, IpEndPointValue? localEndPoint,
            IpEndPointValue remoteEndPoint, bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
            Established.Enqueue((localEndPoint, isNewLocalEndPoint, isNewRemoteEndPoint));
            if (FailEstablished)
                throw new InvalidOperationException("A failed notice for the test.");
        }
    }

    private static ForwardingManager CreateForwardingManager(IPacketForwarderCallbacks callbacks, bool isPingSupported)
    {
        return new ForwardingManager(new BindingSocketFactory(new TestSocketFactory()),
            new ForwardingManagerOptions {
                IsPingSupported = isPingSupported,
                PacketForwarderCallbacks = callbacks,
                UdpTimeout = TransportDefaults.UdpTimeout,
                IcmpTimeout = TransportDefaults.IcmpTimeout,
                MaxUdpClientCount = TransportDefaults.MaxUdpClientCount,
                MaxPingClientCount = TransportDefaults.MaxPingClientCount,
                PacketQueueCapacity = TransportDefaults.PacketForwardingQueueCapacity,
                UdpBufferSize = null,
                LogScope = null
            });
    }

    [TestMethod]
    public async Task An_accepted_destination_is_asked_for_once()
    {
        // its check was asked again at each packet, the server's net-scan count with it
        var callbacks = new RecordingForwarderCallbacks();
        using var forwardingManager = CreateForwardingManager(callbacks, isPingSupported: true);
        forwardingManager.PacketReceived += (_, reply) => reply.Dispose();
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        for (var i = 0; i < 2; i++) {
            forwardingManager.SendPacketQueued(PacketBuilder.BuildUdp(IPEndPoint.Parse("10.0.0.2:2000"),
                peer.Client.GetLocalEndPoint(), new byte[16]));

            forwardingManager.SendPacketQueued(PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
                IPAddress.Loopback, []));
        }

        await WaitFor(() => callbacks.Established.Count == 2, "Each destination should be established once.");
        Assert.AreEqual(2, callbacks.RequestedCount);
    }

    [TestMethod]
    public async Task A_pings_notice_names_no_local_endpoint()
    {
        // it named the ping's source address with port 0, as a new local endpoint
        var callbacks = new RecordingForwarderCallbacks();
        using var forwardingManager = CreateForwardingManager(callbacks, isPingSupported: true);
        forwardingManager.PacketReceived += (_, reply) => reply.Dispose();

        forwardingManager.SendPacketQueued(PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
            IPAddress.Loopback, []));

        await WaitFor(() => callbacks.Established.Count == 1, "The ping's destination should be established.");
        var established = callbacks.Established.Single();
        Assert.IsNull(established.LocalEndPoint);
        Assert.IsFalse(established.IsNewLocal);
        Assert.IsTrue(established.IsNewRemote);
    }

    [TestMethod]
    public async Task A_ping_whose_notice_fails_reaches_no_worker()
    {
        // the notice came after the hand-off, so its caller disposed a packet the worker read
        var callbacks = new RecordingForwarderCallbacks { FailEstablished = true };
        using var forwardingManager = CreateForwardingManager(callbacks, isPingSupported: true);
        forwardingManager.PacketReceived += (_, reply) => reply.Dispose();

        try {
            forwardingManager.SendPacketQueued(PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
                IPAddress.Loopback, []));
        }
        catch (InvalidOperationException) {
            // the notice's failure, where the transport passes it on
        }

        await WaitFor(() => callbacks.Established.Count == 1, "The notice should be raised.");
        Assert.AreEqual(0, GetPingPool(forwardingManager).ClientCount);
    }

    [TestMethod]
    public async Task A_ping_worker_in_use_is_not_swept()
    {
        using var forwardingManager = CreateForwardingManager(isPingSupported: true);
        var replied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        forwardingManager.PacketReceived += (_, reply) => {
            reply.Dispose();
            replied.TrySetResult();
        };

        forwardingManager.SendPacketQueued(PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
            IPAddress.Loopback, []));

        await replied.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The sweep of idle workers, a moment after a ping: the worker had no time of its last use, so the sweep took
        // it for one unused for five minutes, whatever its use
        var pingPool = GetPingPool(forwardingManager);
        var cleanup = typeof(PingForwarderPool).GetMethod("Cleanup", BindingFlags.NonPublic | BindingFlags.Instance,
                          [typeof(CancellationToken)])
                      ?? throw new InvalidOperationException("PingForwarderPool has no Cleanup(CancellationToken).");
        cleanup.Invoke(pingPool, [CancellationToken.None]);

        Assert.AreEqual(1, pingPool.ClientCount, "A worker used a moment ago should stay.");
    }

    [TestMethod]
    public async Task A_ping_worker_unused_for_its_time_is_swept()
    {
        using var pingPool = new PingForwarderPool(new PingForwarderPoolOptions {
            PacketForwarderCallbacks = null,
            IcmpTimeout = TransportDefaults.IcmpTimeout,
            MaxClientCount = TransportDefaults.MaxPingClientCount,
            LogScope = null
        });

        var replied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pingPool.PacketReceived += (_, reply) => {
            reply.Dispose();
            replied.TrySetResult();
        };

        pingPool.SendPacketQueued(PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
            IPAddress.Loopback, []));

        await replied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitFor(() => pingPool.ClientCount == 1, "The ping should have a worker.");

        // a sweep within the worker's time keeps it, and one past its time takes it, once the worker's send has ended
        pingPool.Cleanup(DateTime.UtcNow + TimeSpan.FromMinutes(4));
        Assert.AreEqual(1, pingPool.ClientCount);
        await WaitFor(() => {
            pingPool.Cleanup(DateTime.UtcNow + TimeSpan.FromMinutes(6));
            return pingPool.ClientCount == 0;
        }, "A worker unused past its time should be swept.");
    }

    [TestMethod]
    public async Task A_ping_worker_held_on_its_reply_is_not_swept_past_its_time()
    {
        // it had no time of its last use, so the sweep took it whatever its use
        using var pingPool = new PingForwarderPool(new PingForwarderPoolOptions {
            PacketForwarderCallbacks = null,
            IcmpTimeout = TransportDefaults.IcmpTimeout,
            MaxClientCount = TransportDefaults.MaxPingClientCount,
            LogScope = null
        });

        var replyReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var replyRelease = new ManualResetEventSlim();
        pingPool.PacketReceived += (_, reply) => {
            reply.Dispose();
            replyReached.TrySetResult();
            replyRelease.Wait(TimeSpan.FromSeconds(10)); // the worker's send waits here
        };

        pingPool.SendPacketQueued(PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
            IPAddress.Loopback, []));

        await replyReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        pingPool.Cleanup(DateTime.UtcNow + TimeSpan.FromMinutes(6));
        Assert.AreEqual(1, pingPool.ClientCount, "A worker under way should stay.");

        replyRelease.Set();
        await WaitFor(() => {
            pingPool.Cleanup(DateTime.UtcNow + TimeSpan.FromMinutes(6));
            return pingPool.ClientCount == 0;
        }, "A worker unused past its time should be swept.");
    }

    [TestMethod]
    public async Task A_channel_added_twice_is_counted_once_and_keeps_running()
    {
        using var forwardingManager = CreateForwardingManager();
        var hostToTunnel = new System.IO.Pipelines.Pipe();
        var tunnelToHost = new System.IO.Pipelines.Pipe();
        var channel = new StreamForwardingChannel("live-channel",
            new TestStreamConnection(new DuplexStream(hostToTunnel.Reader.AsStream(), tunnelToHost.Writer.AsStream())),
            new TestStreamConnection(new DuplexStream(tunnelToHost.Reader.AsStream(), hostToTunnel.Writer.AsStream())),
            new TransferBufferSize(4096, 4096));

        forwardingManager.AddChannel(channel, disposeOnFail: true);

        // The second add of the same channel fails, and was counted all the same, with its disposal of the channel the
        // first add started
        Assert.ThrowsExactly<InvalidOperationException>(() => forwardingManager.AddChannel(channel, disposeOnFail: true));
        Assert.AreEqual(1, forwardingManager.TcpConnectionCount);
        Assert.AreEqual(PacketChannelState.Connected, channel.State);

        channel.Dispose();
        forwardingManager.CleanupChannels();
        Assert.AreEqual(0, forwardingManager.TcpConnectionCount);
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task A_packet_channel_a_tunnel_refused_keeps_no_receiver_of_it()
    {
        // the tunnel's receiver stayed on the channel's one slot
        var pipe = new System.IO.Pipelines.Pipe();
        await using var channel = new StreamPacketChannel(new StreamPacketChannelOptions {
            RequestTime = DateTime.UtcNow,
            StreamConnection = new TestStreamConnection(new DuplexStream(pipe.Reader.AsStream(), Stream.Null)),
            Blocking = false,
            AutoDisposePackets = true,
            Lifespan = null,
            ChannelId = Guid.NewGuid().ToString(),
            TrafficMeter = new TrafficMeter()
        });

        channel.Start();

        using var tunnel = new Tunnel(new TunnelOptions {
            MaxPacketChannelCount = TransportDefaults.MaxPacketChannelCount,
            Mtu = TransportDefaults.MtuClient,
            UdpChannel = null
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => tunnel.AddChannel(channel));
        channel.PacketReceived += (_, ipPacket) => ipPacket.Dispose();
    }

    [TestMethod]
    public void A_channel_whose_start_fails_is_not_counted()
    {
        using var forwardingManager = CreateForwardingManager();
        var channel = new StreamForwardingChannel("disposed-channel",
            new TestStreamConnection(new MemoryStream()), new TestStreamConnection(new MemoryStream()),
            new TransferBufferSize(4096, 4096));

        channel.Dispose();

        // a closed channel's start fails, and the manager counted it until a sweep found it disposed
        Assert.ThrowsExactly<ObjectDisposedException>(() => forwardingManager.AddChannel(channel, disposeOnFail: false));
        Assert.AreEqual(0, forwardingManager.TcpConnectionCount);
    }

    [TestMethod]
    [DataRow(2047, 4096)]
    [DataRow(4096, 81921)]
    public void A_buffer_size_out_of_its_range_names_its_parameter(int receive, int send)
    {
        // the message went where the parameter's name belongs
        var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StreamForwardingChannel("test",
            new TestStreamConnection(new MemoryStream()), new TestStreamConnection(new MemoryStream()),
            new TransferBufferSize(send, receive)));

        Assert.AreEqual("tunnelBufferSize", ex.ParamName);
        Assert.AreEqual(receive != 4096 ? receive : send, ex.ActualValue);
        Assert.Contains("from 2048 to 81920", ex.Message);
    }

    [TestMethod]
    public void The_buffer_sizes_at_both_ends_of_their_range_are_taken()
    {
        using var smallest = new StreamForwardingChannel("test",
            new TestStreamConnection(new MemoryStream()), new TestStreamConnection(new MemoryStream()),
            new TransferBufferSize(2048, 2048));

        using var largest = new StreamForwardingChannel("test",
            new TestStreamConnection(new MemoryStream()), new TestStreamConnection(new MemoryStream()),
            new TransferBufferSize(81920, 81920));
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
