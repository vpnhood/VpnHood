using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.DatagramMessaging;
using VpnHood.Core.Tunneling.WebSockets;
using VpnHood.Test.Packets;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
[SuppressMessage("ReSharper", "DisposeOnUsingVariable")]
public class TunnelTest : TestBase
{
    private class ServerUdpChannelTransmitterTest(UdpClient udpClient)
        : UdpChannelTransmitter(udpClient)
    {
        private readonly Dictionary<ulong, SessionUdpTransport> _transports = new();

        public SessionUdpTransport AddSession(ulong sessionId, byte[] sessionKey)
        {
            var transport = new SessionUdpTransport(this, sessionId, sessionKey, null, true);
            _transports[sessionId] = transport;
            return transport;
        }

        protected override SessionUdpTransport? SessionIdToUdpTransport(ulong sessionId)
        {
            return _transports.GetValueOrDefault(sessionId);
        }

        public override void Dispose()
        {
            foreach (var transport in _transports.Values)
                transport.Dispose();

            _transports.Clear();
            base.Dispose();
        }
    }

    private class ClientUdpChannelTransmitterTest : UdpChannelTransmitter
    {
        public SessionUdpTransport UdpTransport { get; }

        public ClientUdpChannelTransmitterTest(IPEndPoint remoteEndPoint, ulong sessionId, byte[] sessionKey)
            : base(new UdpClient(0, remoteEndPoint.AddressFamily))
        {
            UdpTransport = new SessionUdpTransport(this, sessionId, sessionKey, remoteEndPoint, false);
        }

        protected override SessionUdpTransport SessionIdToUdpTransport(ulong sessionId)
        {
            _ = sessionId;
            return UdpTransport;
        }

        public override void Dispose()
        {
            UdpTransport.Dispose();
            base.Dispose();
        }
    }

    private static (UdpChannel Channel, ClientUdpChannelTransmitterTest Transmitter) CreateClientUdpChannel(
        IPEndPoint serverEndPoint, ulong sessionId, byte[] sessionKey)
    {
        var transmitter = new ClientUdpChannelTransmitterTest(serverEndPoint, sessionId, sessionKey);
        var channel = new UdpChannel(transmitter.UdpTransport, new UdpChannelOptions {
            AutoDisposePackets = true,
            Blocking = false,
            ChannelId = Guid.CreateVersion7().ToString(),
            Lifespan = null,
            TrafficMeter = new TrafficMeter()
        });

        return (channel, transmitter);
    }

    private static (UdpChannel Channel, ServerUdpChannelTransmitterTest Transmitter) CreateServerUdpChannel(
        ulong sessionId, byte[] sessionKey)
    {
        var serverTransmitter =
            new ServerUdpChannelTransmitterTest(new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)));
        var channel = new UdpChannel(serverTransmitter.AddSession(sessionId, sessionKey),
            new UdpChannelOptions {
                AutoDisposePackets = true,
                Blocking = false,
                ChannelId = Guid.CreateVersion7().ToString(),
                Lifespan = null,
                TrafficMeter = new TrafficMeter()
            });

        return (channel, serverTransmitter);
    }


    [TestMethod]
    public async Task UdpChannel_Direct()
    {
        // test packets
        var packets = new List<IpPacket> {
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)),
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)),
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true))
        };

        var sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        var (serverUdpChannel, serverTransmitter) = CreateServerUdpChannel(sessionId, sessionKey);
        using var serverTransmitterDisposable = serverTransmitter;
        using var serverUdpChannelDisposable = serverUdpChannel;
        var serverEndPoint = serverTransmitter.LocalEndPoint;
        var serverReceivedPackets = new List<IpPacket>();
        serverUdpChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
            // ReSharper disable once AccessToDisposedClosure
            serverUdpChannel.SendPacketQueued(ipPacket.Clone());
        };
        serverUdpChannel.Start();

        // Create client
        var (clientUdpChannel, clientTransmitter) =
            CreateClientUdpChannel(serverEndPoint, sessionId: sessionId, sessionKey: sessionKey);
        using var clientTransmitterDisposable = clientTransmitter;
        using var clientUdpChannelDisposable = clientUdpChannel;
        var clientReceivedPackets = new List<IpPacket>();
        clientUdpChannelDisposable.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
        };
        clientUdpChannelDisposable.Start();

        // send packet to server through channel
        foreach (var ipPacket in packets)
            clientUdpChannelDisposable.SendPacketQueued(ipPacket.Clone());

        await VhTestUtil.AssertEqualsWait(packets.Count, () => serverReceivedPackets.Count, 
            cancellationToken: TestCt);

        await VhTestUtil.AssertEqualsWait(packets.Count, () => clientReceivedPackets.Count, 
            cancellationToken: TestCt);
    }

    // Regression: a packet arriving for a session whose cryptor was already disposed must not take the
    // shared transmitter down with it. Live on 2026-08-29/30 this killed server-wide UDP for every
    // session until the process restarted, because ObjectDisposedException counted as a dead socket.
    [TestMethod]
    public async Task UdpChannel_survives_a_disposed_session_cryptor()
    {
        var sessionKey = VhUtils.GenerateKey();
        const ulong closingSessionId = 1UL;
        const ulong livingSessionId = 2UL;

        // one server transmitter shared by two sessions
        using var serverTransmitter =
            new ServerUdpChannelTransmitterTest(new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)));
        var serverEndPoint = serverTransmitter.LocalEndPoint;
        var closingTransport = serverTransmitter.AddSession(closingSessionId, sessionKey);

        using var livingChannel = new UdpChannel(serverTransmitter.AddSession(livingSessionId, sessionKey),
            new UdpChannelOptions {
                AutoDisposePackets = true,
                Blocking = false,
                ChannelId = Guid.CreateVersion7().ToString(),
                Lifespan = null,
                TrafficMeter = new TrafficMeter()
            });
        var livingReceivedPackets = new List<IpPacket>();
        livingChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            livingReceivedPackets.Add(ipPacket);
        };
        livingChannel.Start();

        // the closing session tears down its cryptors while its transport is still reachable by sessionId
        closingTransport.Dispose();

        // a late packet for that session hits the disposed cryptor inside the shared read loop
        var (closingClientChannel, closingClientTransmitter) =
            CreateClientUdpChannel(serverEndPoint, sessionId: closingSessionId, sessionKey: sessionKey);
        using var closingClientTransmitterDisposable = closingClientTransmitter;
        using var closingClientChannelDisposable = closingClientChannel;
        closingClientChannel.Start();
        closingClientChannel.SendPacketQueued(PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)));

        // the untouched session must keep flowing over the very same transmitter
        var (livingClientChannel, livingClientTransmitter) =
            CreateClientUdpChannel(serverEndPoint, sessionId: livingSessionId, sessionKey: sessionKey);
        using var livingClientTransmitterDisposable = livingClientTransmitter;
        using var livingClientChannelDisposable = livingClientChannel;
        livingClientChannel.Start();
        livingClientChannel.SendPacketQueued(PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)));

        await VhTestUtil.AssertEqualsWait(1, () => livingReceivedPackets.Count,
            cancellationToken: TestCt);
        Assert.IsTrue(serverTransmitter.Connected,
            "One session's disposed cryptor must not dispose the shared transmitter.");
    }

    [TestMethod]
    public async Task UdpChannel_via_Tunnel()
    {
        VhLogger.MinLogLevel = LogLevel.Trace;

        var waitHandle = new EventWaitHandle(true, EventResetMode.AutoReset);
        waitHandle.Reset();

        // test packets
        var ipPackets = new List<IpPacket> {
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)),
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)),
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true))
        };

        var sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        var (serverUdpChannel, serverTransmitter) =
            CreateServerUdpChannel(sessionId: sessionId, sessionKey: sessionKey);
        using var serverTransmitterDisposable = serverTransmitter;
        using var serverUdpChannelDisposable = serverUdpChannel;
        var serverReceivedPackets = new List<IpPacket>();

        // Create server tunnel
        var serverTunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        serverTunnel.AddChannel(serverUdpChannel);
        serverTunnel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
            // ReSharper disable once AccessToDisposedClosure
            serverUdpChannel.SendPacketQueued(ipPacket.Clone());
        };

        // Create client
        var (clientUdpChannel, clientTransmitter) = CreateClientUdpChannel(serverTransmitter.LocalEndPoint,
            sessionId: sessionId, sessionKey: sessionKey);
        using var clientTransmitterDisposable = clientTransmitter;
        using var clientUdpChannelDisposable = clientUdpChannel;

        // Create client tunnel
        var clientReceivedPackets = new List<IpPacket>();
        var clientTunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        clientTunnel.AddChannel(clientUdpChannelDisposable);
        clientTunnel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
            waitHandle.Set();
        };

        // send packet to server through tunnel
        foreach (var ipPacket in ipPackets)
            clientTunnel.SendPacketQueued(ipPacket);

        await VhTestUtil.AssertEqualsWait(ipPackets.Count, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        await VhTestUtil.AssertEqualsWait(ipPackets.Count, () => clientReceivedPackets.Count, cancellationToken: TestCt);
    }

    private static async Task SimpleLoopback(TcpListener tcpListener, CancellationToken cancellationToken)
    {
        using var client = await tcpListener.AcceptTcpClientAsync(cancellationToken);

        // Create a memory stream to store the incoming data
        ChunkStream chunkStream = new WebSocketStream(client.GetStream(), "Server", true, isServer: true);
        while (true) {
            // echo back the data to the client
            await chunkStream.CopyToAsync(chunkStream, cancellationToken);
            await chunkStream.DisposeAsync();
            if (!chunkStream.CanReuse)
                break;

            chunkStream = await chunkStream.CreateReuse();
        }

        Assert.IsFalse(chunkStream.CanReuse);
    }

    private static async Task CheckStreamEcho(Stream stream, CancellationToken cancellationToken)
    {
        // read o data from stream
        var readBuffer = new byte[10 * 1024 * 1024 + 2000].AsMemory(); // 10MB buffer size
        var readingTask = stream.ReadExactlyAsync(readBuffer, cancellationToken);

        // write data in two parts
        var writeBuffer = new byte[readBuffer.Length]; // 10MB buffer size
        var random = new Random();
        random.NextBytes(writeBuffer);
        using var writeStream = new MemoryStream(writeBuffer);

        // write the length of the data
        await writeStream.CopyToAsync(stream, cancellationToken);

        // wait for reading task to complete
        await readingTask;
        await stream.DisposeAsync();

        // check the read data
        CollectionAssert.AreEqual(readBuffer.ToArray(), writeBuffer);

        // make sure that the stream is closed
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            stream.ReadAsync(readBuffer, cancellationToken).AsTask());
    }


    [TestMethod]
    public async Task ChunkStream_WebSocket()
    {
        // create server
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        var cts = new CancellationTokenSource();
        tcpListener.Start();
        _ = SimpleLoopback(tcpListener, cts.Token);

        // connect to server
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync((IPEndPoint)tcpListener.LocalEndpoint, cts.Token);
        var stream = tcpClient.GetStream();

        // send data
        await using var binaryStream = new WebSocketStream(stream, "client", true, isServer: false);

        // check stream by echo
        await CheckStreamEcho(binaryStream, cts.Token);
        await CheckStreamEcho(await binaryStream.CreateReuse(), cts.Token);
        await CheckStreamEcho(await binaryStream.CreateReuse(), cts.Token);
        Assert.IsTrue(binaryStream.CanReuse);

        // read data from stream
        await cts.CancelAsync();
        tcpListener.Stop();
    }

    // connects the client over loopback and returns the server's end
    private async Task<TcpStreamConnection> Accept(TcpClient client)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try {
            var acceptTask = listener.AcceptTcpClientAsync(TestCt).AsTask();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, TestCt);
            return new TcpStreamConnection(await acceptTask, connectionName: "test", isServer: true);
        }
        finally {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Reuse_prevented_through_a_wrapper_closes_the_connection_at_its_disposal()
    {
        using var client = new TcpClient();
        var tcpConnection = await Accept(client);

        // wrapped as the server wraps a WebSocket connection: a reusable one under its own decorator
        var webSocketStream = new WebSocketStream(tcpConnection.Stream, tcpConnection.ConnectionId, useBuffer: false,
            isServer: true);
        var connection = new StreamConnectionDecorator(
            new ReusableStreamConnection(tcpConnection, webSocketStream, reused => reused.Dispose()));

        connection.PreventReuse();
        connection.Dispose();

        // the socket closes at once: the client, which sends nothing more, reads to its end, where a release would
        // first wait for the client to end the logical stream
        var clientStream = client.GetStream();
        var buffer = new byte[1024];
        while (await clientStream.ReadAsync(buffer, TestCt).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCt) > 0) {
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Disposal_hands_a_cleanly_ended_connection_to_its_owner(bool disposeAsync)
    {
        using var tcpClient = new TcpClient();
        var serverTransport = await Accept(tcpClient);
        var clientTransport = new TcpStreamConnection(tcpClient, connectionName: "test", isServer: false);

        // each end as a client and a server wrap a WebSocket connection
        var serverReused = new TaskCompletionSource<ReusableStreamConnection>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var clientReused = new TaskCompletionSource<ReusableStreamConnection>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serverConnection = new ReusableStreamConnection(serverTransport,
            new WebSocketStream(serverTransport.Stream, "server", useBuffer: false, isServer: true),
            reused => serverReused.TrySetResult(reused));
        var clientConnection = new ReusableStreamConnection(clientTransport,
            new WebSocketStream(clientTransport.Stream, "client", useBuffer: false, isServer: false),
            reused => clientReused.TrySetResult(reused));

        var buffer = new byte[1];
        await clientConnection.Stream.WriteAsync(new byte[] { 1 }, TestCt);
        await serverConnection.Stream.ReadExactlyAsync(buffer, TestCt);
        Assert.AreEqual(1, buffer[0]);

        // the logical stream ends once both ends have released it, which Dispose leaves to run
        if (disposeAsync) {
            await Task.WhenAll(serverConnection.DisposeAsync().AsTask(), clientConnection.DisposeAsync().AsTask())
                .WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        }
        else {
            serverConnection.Dispose();
            clientConnection.Dispose();
        }

        // the transport carries the next one, closed at the end of the test
        using var serverNext = await serverReused.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        using var clientNext = await clientReused.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        serverNext.PreventReuse();
        clientNext.PreventReuse();
        await clientNext.Stream.WriteAsync(new byte[] { 2 }, TestCt);
        await serverNext.Stream.ReadExactlyAsync(buffer, TestCt);
        Assert.AreEqual(2, buffer[0]);
    }

    // a connection that records which of its closes ran
    private sealed class CloseRecordingConnection : IStreamConnection
    {
        public bool SyncClosed { get; private set; }
        public TaskCompletionSource AsyncClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ConnectionId { get; set; } = "test";
        public string ConnectionName => "test";
        public bool IsServer => false;
        public bool Connected => !SyncClosed && !AsyncClosed.Task.IsCompleted;
        public Stream Stream { get; } = new MemoryStream();
        public IPEndPoint LocalEndPoint { get; } = new(IPAddress.Loopback, 1);
        public IPEndPoint RemoteEndPoint { get; } = new(IPAddress.Loopback, 2);
        public bool RequireHttpResponse { get; set; }
        public void PreventReuse() { }
        public void Dispose() => SyncClosed = true;

        public ValueTask DisposeAsync()
        {
            AsyncClosed.TrySetResult();
            return default;
        }
    }

    // An owner closes its connection through the async dispose: the sync one may wait for the peer, as a QUIC
    // stream's does, and a channel is disposed under the tunnel's lock and on its packet path
    [TestMethod]
    public async Task Packet_channel_closes_its_connection_through_its_async_dispose()
    {
        var connection = new CloseRecordingConnection();
        var channel = new StreamPacketChannel(new StreamPacketChannelOptions {
            StreamConnection = connection,
            ChannelId = "test",
            RequestTime = DateTime.UtcNow,
            AutoDisposePackets = true,
            Blocking = false,
            Lifespan = null
        });

        channel.Dispose();

        await connection.AsyncClosed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsFalse(connection.SyncClosed);
    }

    [TestMethod]
    public async Task Proxy_channel_closes_its_connections_through_their_async_dispose()
    {
        var hostConnection = new CloseRecordingConnection();
        var tunnelConnection = new CloseRecordingConnection();
        var channel = new ProxyChannel("test", hostConnection, tunnelConnection, new TransferBufferSize(4096, 4096));

        channel.Dispose();

        await Task.WhenAll(hostConnection.AsyncClosed.Task, tunnelConnection.AsyncClosed.Task)
            .WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsFalse(hostConnection.SyncClosed);
        Assert.IsFalse(tunnelConnection.SyncClosed);
    }

    [TestMethod]
    public async Task Reusable_connection_closes_its_transport_through_its_async_dispose()
    {
        var transport = new CloseRecordingConnection();
        var connection = new ReusableStreamConnection(transport,
            new WebSocketStream(transport.Stream, "test", useBuffer: false, isServer: false), reuseCallback: null);

        connection.Dispose();

        await transport.AsyncClosed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsFalse(transport.SyncClosed);
    }

    [TestMethod]
    public async Task WebSocketHeader_build_Client()
    {
        Memory<byte> writeBuffer = new byte[150];
        Memory<byte> readBuffer = new byte[150];

        for (var i = 0; i < 0xFFFF + 100; i++) {
            WebSocketUtils.BuildWebSocketFrameHeader(writeBuffer.Span[..14], i, new byte[4]);
            using var memStream = new MemoryStream(writeBuffer[..14].ToArray());
            var header = await WebSocketUtils.ReadWebSocketHeader(memStream, readBuffer, TestCt);
            Assert.AreEqual(i, header.PayloadLength);
            Assert.IsTrue(header.IsBinary);

            // Assert the stream position based on WebSocket payload length
            if (i <= 125)
                Assert.AreEqual(2 + 4, memStream.Position, "Stream position mismatch for payload length <= 125");
            else if (i <= 0xFFFF)
                Assert.AreEqual(4 + 4, memStream.Position, "Stream position mismatch for payload length <= 0xFFFF");
            else
                Assert.AreEqual(10 + 4, memStream.Position, "Stream position mismatch for payload length > 0xFFFF");
        }
    }

    [TestMethod]
    public async Task WebSocketHeader_build_Server()
    {
        Memory<byte> writeBuffer = new byte[150];
        Memory<byte> readBuffer = new byte[150];

        for (var i = 0; i < 0xFFFF + 100; i++) {
            WebSocketUtils.BuildWebSocketFrameHeader(writeBuffer.Span[..14], i);
            using var memStream = new MemoryStream(writeBuffer[..14].ToArray());
            var header = await WebSocketUtils.ReadWebSocketHeader(memStream, readBuffer, TestCt);
            Assert.AreEqual(i, header.PayloadLength);
            Assert.IsTrue(header.IsBinary);

            // Assert the stream position based on WebSocket payload length
            if (i <= 125)
                Assert.AreEqual(2, memStream.Position, "Stream position mismatch for payload length <= 125");
            else if (i <= 0xFFFF)
                Assert.AreEqual(4, memStream.Position, "Stream position mismatch for payload length <= 0xFFFF");
            else
                Assert.AreEqual(10, memStream.Position, "Stream position mismatch for payload length > 0xFFFF");
        }
    }

    [TestMethod]
    public void A_channel_added_to_a_full_tunnel_takes_a_closing_channels_place()
    {
        // a server's tunnel with all its channels, one of them closing as the client replaces it
        using var tunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        var receivedPorts = new List<int>();
        tunnel.PacketReceived += (_, ipPacket) => {
            receivedPorts.Add(ipPacket.ExtractUdp().SourcePort);
            ipPacket.Dispose();
        };
        var channels = Enumerable.Range(0, tunnel.MaxPacketChannelCount).Select(_ => new TestPacketChannel()).ToArray();
        foreach (var channel in channels)
            tunnel.AddChannel(channel);
        var closingChannel = channels[2];
        closingChannel.Receive(PacketMessageHandler.CreateMessage(new ClosePacketMessage()));

        // the replacement takes the closing channel's place, and every live channel stays
        var replacement = new TestPacketChannel();
        tunnel.AddChannel(replacement);
        var liveChannels = channels.Where(x => x != closingChannel).Append(replacement).ToArray();
        CollectionAssert.AreEquivalent(liveChannels, tunnel.GetPacketChannels().ToArray());
        Assert.IsTrue(liveChannels.All(x => x.State == PacketChannelState.Connected));

        // the closing channel is left to its grace: what still arrives on it reaches the tunnel
        Assert.AreEqual(PacketChannelState.Disconnecting, closingChannel.State);
        closingChannel.Receive(PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"), 1, 53,
            new byte[16]));
        CollectionAssert.AreEqual(new[] { 1 }, receivedPorts);
    }

    [TestMethod]
    [DataRow(8, 1)] // one channel its peer ended
    [DataRow(8, 8)] // every channel ended by its peer at once
    [DataRow(1, 1)] // the one channel replaced
    public void A_channel_added_to_a_full_tunnel_takes_the_place_of_disposed_ones(int maxChannelCount,
        int disposedCount)
    {
        // a server's tunnel with all its channels, its last ones disposed but still listed, as a channel whose peer
        // ended it is until the next cleanup
        using var tunnel = new Tunnel(TestHelper.CreateTunnelOptions()) { MaxPacketChannelCount = maxChannelCount };
        var channels = Enumerable.Range(0, maxChannelCount).Select(_ => new TestPacketChannel()).ToArray();
        foreach (var channel in channels)
            tunnel.AddChannel(channel);
        var disposedChannels = channels.Skip(maxChannelCount - disposedCount).ToArray();
        foreach (var channel in disposedChannels)
            channel.Dispose();

        // the replacement takes their place, and every live channel stays
        var replacement = new TestPacketChannel();
        tunnel.AddChannel(replacement);
        var liveChannels = channels.Except(disposedChannels).Append(replacement).ToArray();
        CollectionAssert.AreEquivalent(liveChannels, tunnel.GetPacketChannels().ToArray());
        Assert.IsTrue(liveChannels.All(x => x.State == PacketChannelState.Connected));
    }

    [TestMethod]
    public void Packet_channels_read_while_the_list_changes_are_a_copy()
    {
        using var tunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        tunnel.AddChannel(new TestPacketChannel());

        // a reader part-way through the channels as an add, here or on another thread, changes the list
        using var reader = tunnel.GetPacketChannels().GetEnumerator();
        Assert.IsTrue(reader.MoveNext());
        tunnel.AddChannel(new TestPacketChannel());

        // it reads on through its own copy, and the next read has both
        Assert.IsFalse(reader.MoveNext());
        Assert.AreEqual(2, tunnel.GetPacketChannels().Count);
    }

    // a channel whose disposal adds another to its tunnel: an add in the middle of the tunnel's disposal, as one
    // waiting on the channel list's lock gets
    private class ChannelAddingOnDispose(Tunnel tunnel, IChannel channel) : TestPacketChannel
    {
        public Exception? AddError { get; private set; }

        protected override void DisposeManaged()
        {
            try {
                tunnel.AddChannel(channel, disposeIfFailed: true);
            }
            catch (Exception ex) {
                AddError = ex;
            }

            base.DisposeManaged();
        }
    }

    [TestMethod]
    public void A_channel_added_while_the_tunnel_is_disposed_is_refused_and_disposed()
    {
        using var tunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        var lateChannel = new TestPacketChannel();
        var addingChannel = new ChannelAddingOnDispose(tunnel, lateChannel);
        tunnel.AddChannel(addingChannel);

        tunnel.Dispose();

        Assert.IsInstanceOfType<ObjectDisposedException>(addingChannel.AddError);
        Assert.AreEqual(PacketChannelState.Disposed, lateChannel.State);
    }
}