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
    private static IpPacket CreateRandomPacket()
    {
        return PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true));
    }

    [TestMethod]
    public async Task UdpChannel_Direct()
    {
        var packets = new List<IpPacket> { CreateRandomPacket(), CreateRandomPacket(), CreateRandomPacket() };
        const ulong sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var listener = new TestUdpListener();
        var serverChannel = listener.AddSession(sessionId, sessionKey);
        var serverReceivedPackets = new List<IpPacket>();
        serverChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
            // ReSharper disable once AccessToDisposedClosure
            serverChannel.SendPacketQueued(ipPacket.Clone());
        };

        using var clientChannel = UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey);
        var clientReceivedPackets = new List<IpPacket>();
        clientChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
        };

        foreach (var ipPacket in packets)
            clientChannel.SendPacketQueued(ipPacket.Clone());

        await VhTestUtil.AssertEqualsWait(packets.Count, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        await VhTestUtil.AssertEqualsWait(packets.Count, () => clientReceivedPackets.Count, cancellationToken: TestCt);
    }

    // Regression: a datagram arriving for a session whose keys were already disposed must not take the shared listener
    // down with it. Live on 2026-08-29/30 this killed server-wide UDP for every session until the process restarted,
    // because ObjectDisposedException counted as a dead socket
    [TestMethod]
    public async Task UdpListener_survives_a_disposed_session()
    {
        var sessionKey = VhUtils.GenerateKey();
        const ulong closingSessionId = 1UL;
        const ulong livingSessionId = 2UL;

        // one listener shared by two sessions
        using var listener = new TestUdpListener();
        var closingChannel = listener.AddSession(closingSessionId, sessionKey);
        var livingChannel = listener.AddSession(livingSessionId, sessionKey);
        var livingReceivedPackets = new List<IpPacket>();
        livingChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            livingReceivedPackets.Add(ipPacket);
        };

        // the closing session's keys go while its channel is still reachable by its id
        closingChannel.Dispose();

        // a late datagram for that session meets the disposed keys inside the shared read loop
        using var closingClientChannel =
            UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, closingSessionId, sessionKey);
        closingClientChannel.SendPacketQueued(CreateRandomPacket());

        // the other session must keep flowing over the very same listener
        using var livingClientChannel =
            UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, livingSessionId, sessionKey);
        livingClientChannel.SendPacketQueued(CreateRandomPacket());

        await VhTestUtil.AssertEqualsWait(1, () => livingReceivedPackets.Count, cancellationToken: TestCt);
        Assert.IsTrue(listener.Transmitter.Connected,
            "One session's disposed keys must not dispose the shared listener.");
    }

    // A forged datagram, the session's id with a random tag, must change nothing on the server: its downstream still
    // goes to the client
    [TestMethod]
    public async Task UdpChannel_forged_datagram_changes_nothing()
    {
        const ulong sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var listener = new TestUdpListener();
        var serverChannel = listener.AddSession(sessionId, sessionKey);
        var serverReceivedPackets = new List<IpPacket>();
        serverChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
        };

        using var clientChannel = UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey);
        var clientReceivedPackets = new List<IpPacket>();
        clientChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
        };

        // a genuine datagram sets the server's route
        clientChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(1, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        var route = serverChannel.Route;
        Assert.IsNotNull(route);

        // the forged datagram, given time to reach the read loop
        using var forger = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await forger.SendAsync(UdpTestUtil.CreateForgedDatagram(sessionId), listener.LocalEndPoint, TestCt);
        await Task.Delay(300, TestCt);

        // the server's next packet, with no client datagram in between to put the route back
        serverChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(1, () => clientReceivedPackets.Count, cancellationToken: TestCt);
        Assert.AreSame(route, serverChannel.Route);
        await UdpTestUtil.AssertNothingReceived(forger, TestCt);
    }

    // A server's listeners all reach a session's channel. A forged datagram on another one than the route's must
    // neither move the session there nor change its address
    [TestMethod]
    public async Task UdpChannel_forged_datagram_on_another_listener_changes_nothing()
    {
        const ulong sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var listener = new TestUdpListener();
        var serverChannel = listener.AddSession(sessionId, sessionKey);
        var serverReceivedPackets = new List<IpPacket>();
        serverChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
        };

        using var otherListener = new TestUdpListener();
        otherListener.AddForeignSession(serverChannel);

        using var clientChannel = UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey);
        var clientReceivedPackets = new List<IpPacket>();
        clientChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
        };

        // a genuine datagram sets the server's route
        clientChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(1, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        var route = serverChannel.Route;
        Assert.IsNotNull(route);

        // the forged datagram on the other listener, given time to reach its read loop
        using var forger = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await forger.SendAsync(UdpTestUtil.CreateForgedDatagram(sessionId), otherListener.LocalEndPoint, TestCt);
        await Task.Delay(300, TestCt);

        Assert.AreSame(route, serverChannel.Route);
        serverChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(1, () => clientReceivedPackets.Count, cancellationToken: TestCt);
        await UdpTestUtil.AssertNothingReceived(forger, TestCt);
    }

    // The client's socket is replaced, as after a network change: its next datagram, from the new port with its count
    // carrying on, moves the server's route
    [TestMethod]
    public async Task UdpChannel_follows_a_new_client_socket()
    {
        const ulong sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var listener = new TestUdpListener();
        var serverChannel = listener.AddSession(sessionId, sessionKey);
        var serverReceivedPackets = new List<IpPacket>();
        serverChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
        };

        using var clientChannel = UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey);
        var clientReceivedPackets = new List<IpPacket>();
        clientChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
        };

        clientChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(1, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        var firstEndPoint = serverChannel.Route?.EndPoint;
        Assert.IsNotNull(firstEndPoint);

        clientChannel.CloseSocket();
        clientChannel.OpenSocket();
        clientChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(2, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        Assert.AreNotEqual(firstEndPoint, serverChannel.Route?.EndPoint);

        serverChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(1, () => clientReceivedPackets.Count, cancellationToken: TestCt);
    }

    // An older datagram of the session's, from another address, as a reordered one of the client's comes: it goes up,
    // but must not move the server's route, which only the newest datagram does
    [TestMethod]
    public async Task UdpChannel_older_datagram_from_another_address_changes_nothing()
    {
        const ulong sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var listener = new TestUdpListener();
        var serverChannel = listener.AddSession(sessionId, sessionKey);
        var serverReceivedPackets = new List<IpPacket>();
        serverChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
        };

        using var clientChannel = UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey,
            firstSendSequenceNumber: 1000);
        var clientReceivedPackets = new List<IpPacket>();
        clientChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
        };

        clientChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(1, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        var route = serverChannel.Route;
        Assert.IsNotNull(route);

        // the session's datagram with an older seq, never sent before, from another socket
        using var replayChannel = UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey,
            firstSendSequenceNumber: 500);
        replayChannel.SendPacketQueued(CreateRandomPacket());

        // delivered, as a genuine datagram reordered on its way would be, but the route stays
        await VhTestUtil.AssertEqualsWait(2, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        Assert.AreSame(route, serverChannel.Route);
        serverChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(1, () => clientReceivedPackets.Count, cancellationToken: TestCt);
    }

    // A copy of a datagram goes up once, on either side: the test relays between the two, so it holds each side's
    // datagram and the client's address
    [TestMethod]
    public async Task UdpChannel_copy_goes_up_once()
    {
        const ulong sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var listener = new TestUdpListener();
        var serverChannel = listener.AddSession(sessionId, sessionKey);
        var serverReceivedPackets = new List<IpPacket>();
        serverChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
        };

        using var relay = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var clientChannel = UdpTestUtil.CreateClientChannel(relay.Client.GetLocalEndPoint(), sessionId,
            sessionKey);
        await relay.ReceiveAsync(TestCt); // the socket's announcement, which carries no packet
        var clientReceivedPackets = new List<IpPacket>();
        clientChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
        };

        // the client's datagram, twice: the server's route is the relay
        clientChannel.SendPacketQueued(CreateRandomPacket());
        var clientDatagram = await relay.ReceiveAsync(TestCt);
        await relay.SendAsync(clientDatagram.Buffer, listener.LocalEndPoint, TestCt);
        await relay.SendAsync(clientDatagram.Buffer, listener.LocalEndPoint, TestCt);
        await VhTestUtil.AssertEqualsWait(1, () => serverReceivedPackets.Count, cancellationToken: TestCt);

        // the server's datagram, twice, to the client's socket
        serverChannel.SendPacketQueued(CreateRandomPacket());
        var serverDatagram = (await relay.ReceiveAsync(TestCt)).Buffer;
        await relay.SendAsync(serverDatagram, clientDatagram.RemoteEndPoint, TestCt);
        await relay.SendAsync(serverDatagram, clientDatagram.RemoteEndPoint, TestCt);
        await VhTestUtil.AssertEqualsWait(1, () => clientReceivedPackets.Count, cancellationToken: TestCt);
        await Task.Delay(300, TestCt);

        Assert.HasCount(1, serverReceivedPackets);
        Assert.HasCount(1, clientReceivedPackets);
    }

    // A TCP packet channel request carries the seq of the client's next UDP datagram: one it sent before, arriving late,
    // must not move the route, and the ones from that seq on still do
    [TestMethod]
    public async Task UdpChannel_datagram_before_a_tcp_request_changes_nothing()
    {
        const ulong sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var listener = new TestUdpListener();
        var serverChannel = listener.AddSession(sessionId, sessionKey);
        var serverReceivedPackets = new List<IpPacket>();
        serverChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
        };

        using var clientChannel = UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey,
            firstSendSequenceNumber: 1000);
        clientChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(1, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        var route = serverChannel.Route;
        Assert.IsNotNull(route);

        // the request's seq is past a datagram the client sent and the server has not seen yet: 1999, after the late
        // socket's announcement
        serverChannel.ExpireBefore(2000);
        using var lateChannel = UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey,
            firstSendSequenceNumber: 1998);
        lateChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(2, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        Assert.AreSame(route, serverChannel.Route);

        // the client's datagram with the request's seq, as when it turns back to UDP
        using var nextChannel = UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey,
            firstSendSequenceNumber: 2000);
        nextChannel.SendPacketQueued(CreateRandomPacket());
        await VhTestUtil.AssertEqualsWait(3, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        Assert.AreNotSame(route, serverChannel.Route);
    }

    // A datagram the server sent, sent back to it, must not pass as the client's: the server-reply bit in the nonce
    // fails its tag, where its higher seq would otherwise move the route
    [TestMethod]
    public async Task UdpChannel_bounced_server_datagram_changes_nothing()
    {
        const ulong sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var listener = new TestUdpListener();
        var serverChannel = listener.AddSession(sessionId, sessionKey);
        var serverReceivedPackets = new List<IpPacket>();
        serverChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
        };

        // the client's side is a raw socket, so the server's datagram is caught as sent: a genuine client datagram,
        // with the lowest seq, goes through it first
        using var clientSocket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var sealer = UdpTestUtil.CreateClientChannel(clientSocket.Client.GetLocalEndPoint(), sessionId,
            sessionKey, firstSendSequenceNumber: 1);
        await clientSocket.ReceiveAsync(TestCt); // the sealer's announcement, which carries no packet
        sealer.SendPacketQueued(CreateRandomPacket());
        var clientDatagram = (await clientSocket.ReceiveAsync(TestCt)).Buffer;
        await clientSocket.SendAsync(clientDatagram, listener.LocalEndPoint, TestCt);
        await VhTestUtil.AssertEqualsWait(1, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        Assert.AreEqual(clientSocket.Client.GetLocalEndPoint(), serverChannel.Route?.EndPoint);

        // the server's datagram, caught, then sent back to it from another socket
        serverChannel.SendPacketQueued(CreateRandomPacket());
        var serverDatagram = (await clientSocket.ReceiveAsync(TestCt)).Buffer;
        using var bouncer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await bouncer.SendAsync(serverDatagram, listener.LocalEndPoint, TestCt);
        await Task.Delay(300, TestCt);

        Assert.AreEqual(clientSocket.Client.GetLocalEndPoint(), serverChannel.Route?.EndPoint);
        Assert.HasCount(1, serverReceivedPackets);
        await UdpTestUtil.AssertNothingReceived(bouncer, TestCt);
    }

    // the tunnels send on their UDP channels while UDP is on, and hand up what those receive
    [TestMethod]
    public async Task UdpChannel_via_Tunnel()
    {
        var ipPackets = new List<IpPacket> { CreateRandomPacket(), CreateRandomPacket(), CreateRandomPacket() };
        const ulong sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var listener = new TestUdpListener();
        using var serverTunnel = new Tunnel(TestHelper.CreateTunnelOptions(listener.AddSession(sessionId, sessionKey))) {
            UseUdp = true
        };
        var serverReceivedPackets = new List<IpPacket>();
        serverTunnel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
            // ReSharper disable once AccessToDisposedClosure
            serverTunnel.SendPacketQueued(ipPacket.Clone());
        };

        using var clientTunnel = new Tunnel(TestHelper.CreateTunnelOptions(
            UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey))) {
            UseUdp = true
        };
        var clientReceivedPackets = new List<IpPacket>();
        clientTunnel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
        };

        foreach (var ipPacket in ipPackets)
            clientTunnel.SendPacketQueued(ipPacket);

        await VhTestUtil.AssertEqualsWait(ipPackets.Count, () => serverReceivedPackets.Count, cancellationToken: TestCt);
        await VhTestUtil.AssertEqualsWait(ipPackets.Count, () => clientReceivedPackets.Count, cancellationToken: TestCt);
    }

    // a tunnel with no UDP channel cannot turn UDP on
    [TestMethod]
    public void Udp_needs_a_udp_channel()
    {
        using var tunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        Assert.ThrowsExactly<InvalidOperationException>(() => tunnel.UseUdp = true);
        Assert.IsFalse(tunnel.UseUdp);
    }

    // the UDP channel lasts as long as its session, so the manager of the stream channels, which closes and cleans
    // them up, must refuse it
    [TestMethod]
    public void UdpChannel_stays_out_of_the_stream_channels()
    {
        using var listener = new TestUdpListener();
        var udpChannel = listener.AddSession(1UL, VhUtils.GenerateKey());
        using var tunnel = new Tunnel(TestHelper.CreateTunnelOptions(udpChannel));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tunnel.AddChannel(udpChannel));
        Assert.AreEqual(0, tunnel.PacketChannelCount);
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
    public async Task Disposal_hands_a_cleanly_ended_connection_to_its_owner()
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

        // the logical stream ends once both ends have released it
        await Task.WhenAll(serverConnection.DisposeAsync().AsTask(), clientConnection.DisposeAsync().AsTask())
            .WaitAsync(TimeSpan.FromSeconds(5), TestCt);

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
    public void Reusable_connections_quick_close_closes_its_transport_at_once()
    {
        var transport = new CloseRecordingConnection();
        var reused = false;
        var connection = new ReusableStreamConnection(transport,
            new WebSocketStream(transport.Stream, "test", useBuffer: false, isServer: false), _ => reused = true);

        connection.Dispose();

        // through the transport's quick close: no end of the logical stream with the peer, and no reuse
        Assert.IsTrue(transport.SyncClosed);
        Assert.IsFalse(transport.AsyncClosed.Task.IsCompleted);
        Assert.IsFalse(reused);
    }

    [TestMethod]
    public async Task Reusable_connections_quick_close_during_its_release_closes_its_transport()
    {
        using var tcpClient = new TcpClient();
        await using var serverTransport = await Accept(tcpClient);
        var clientTransport = new TcpStreamConnection(tcpClient, connectionName: "test", isServer: false);
        var reused = false;
        var clientConnection = new ReusableStreamConnection(clientTransport,
            new WebSocketStream(clientTransport.Stream, "client", useBuffer: false, isServer: false), _ => reused = true);

        // the release waits for the peer to end the logical stream too, which it does not do here
        var release = clientConnection.DisposeAsync().AsTask();
        clientConnection.Dispose();

        await release.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsFalse(clientTransport.Connected);
        Assert.IsFalse(reused);
    }

    // One that cannot carry the next logical stream, as its peer ended it or a read failed, closes its source, and
    // asynchronously: the sync close waits for a QUIC stream's peer
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task WebSocket_stream_that_cannot_be_reused_closes_its_source_asynchronously(bool useBuffer,
        bool readFails)
    {
        var source = new CloseRecordingStream { FailReads = readFails };
        var stream = new WebSocketStream(source, "test", useBuffer: useBuffer, isServer: false);

        if (readFails)
            await Assert.ThrowsExactlyAsync<IOException>(() => stream.ReadAsync(new byte[10], TestCt).AsTask());
        else
            Assert.AreEqual(0, await stream.ReadAsync(new byte[10], TestCt));
        Assert.IsFalse(stream.CanReuse);

        await stream.DisposeAsync();
        // ReSharper disable once MethodHasAsyncOverload
        stream.Dispose(); // a sync one after it finds the source closed
        Assert.IsTrue(source.AsyncClosed);
        Assert.IsFalse(source.SyncClosed);
    }

    // its end with the peer fails, here on the read for the peer's end
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WebSocket_stream_whose_end_fails_closes_its_source_asynchronously(bool useBuffer)
    {
        var source = new CloseRecordingStream { FailReads = true };
        var stream = new WebSocketStream(source, "test", useBuffer: useBuffer, isServer: false);
        Assert.IsTrue(stream.CanReuse);

        await stream.DisposeAsync();
        Assert.IsFalse(stream.CanReuse);
        Assert.IsTrue(source.AsyncClosed);
        Assert.IsFalse(source.SyncClosed);
    }

    // closing the source synchronously waits for a QUIC stream's peer; its owner closes it asynchronously
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WebSocket_stream_that_cannot_be_reused_leaves_its_source_to_its_owner_on_a_sync_dispose(
        bool useBuffer)
    {
        var source = new CloseRecordingStream();
        var stream = new WebSocketStream(source, "test", useBuffer: useBuffer, isServer: false);
        Assert.AreEqual(0, await stream.ReadAsync(new byte[10], TestCt));

        // ReSharper disable once MethodHasAsyncOverload
        stream.Dispose();
        await stream.DisposeAsync(); // an async one after it leaves the source too
        Assert.IsFalse(source.SyncClosed);
        Assert.IsFalse(source.AsyncClosed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WebSocket_stream_that_cannot_be_reused_closes_its_source_once_on_two_async_disposes_at_once(
        bool useBuffer)
    {
        var source = new CloseRecordingStream { HoldAsyncClose = true };
        var stream = new WebSocketStream(source, "test", useBuffer: useBuffer, isServer: false);
        Assert.AreEqual(0, await stream.ReadAsync(new byte[10], TestCt));

        var first = stream.DisposeAsync().AsTask();
        var second = stream.DisposeAsync().AsTask(); // while the first one's source close is held
        source.ReleaseAsyncClose();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.AreEqual(1, source.AsyncCloseCount);
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
    public async Task Closing_channels_beyond_twice_the_maximum_go_oldest_first()
    {
        // a peer closes the tunnel's channels one after another: three, then four more a second later, as the
        // channels' clock reads to the second
        using var tunnel = new Tunnel(TestHelper.CreateTunnelOptions()) { MaxPacketChannelCount = 2 };
        var olderChannels = AddAndClose(3);
        await Task.Delay(TimeSpan.FromSeconds(1.1), TestCt);
        var newerChannels = AddAndClose(4);

        // the next add leaves the four newer ones to their grace and disposes the three older ones
        tunnel.AddChannel(new TestPacketChannel());
        Assert.IsTrue(newerChannels.All(x => x.State == PacketChannelState.Disconnecting));
        Assert.IsTrue(olderChannels.All(x => x.State == PacketChannelState.Disposed));
        return;

        TestPacketChannel[] AddAndClose(int count)
        {
            var channels = Enumerable.Range(0, count).Select(_ => new TestPacketChannel()).ToArray();
            foreach (var channel in channels) {
                tunnel.AddChannel(channel);
                channel.Close();
            }

            return channels;
        }
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