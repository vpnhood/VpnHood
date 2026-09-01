using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.WebSockets;
using VpnHood.Test.Packets;

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

        binaryStream.PreventReuse();
        await Assert.ThrowsAsync<InvalidOperationException>(binaryStream.CreateReuse);

        // read data from stream
        await cts.CancelAsync();
        tcpListener.Stop();
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

}