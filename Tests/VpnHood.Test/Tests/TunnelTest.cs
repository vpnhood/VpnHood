using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Test.Packets;

namespace VpnHood.Test.Tests;

[TestClass]
[SuppressMessage("ReSharper", "DisposeOnUsingVariable")]
public class TunnelTest : TestBase
{
    private class ServerUdpChannelTransmitterTest(UdpClient udpClient, byte[] serverKey)
        : UdpChannelTransmitter(udpClient, serverKey)
    {
        public UdpChannel? UdpChannel { get; set; }

        protected override void OnReceiveData(ulong sessionId, IPEndPoint remoteEndPoint,
            Memory<byte> buffer, long channelCryptorPosition)
        {
            if (UdpChannel != null) {
                UdpChannel.RemoteEndPoint = remoteEndPoint;
                UdpChannel.OnDataReceived(buffer, channelCryptorPosition);
            }
        }
    }

    private static UdpChannel CreateClientUdpChannel(IPEndPoint serverEndPoint, byte[] serverKey,
        uint sessionId, byte[] sessionKey)
    {
        var clientUdpChannel = ClientUdpChannelFactory.Create(new ClientUdpChannelOptions {
            SessionId = sessionId,
            SessionKey = sessionKey,
            AutoDisposePackets = true,
            Blocking = false,
            ProtocolVersion = 4,
            RemoteEndPoint = serverEndPoint,
            Lifespan = null,
            ChannelId = Guid.CreateVersion7().ToString(),
            SocketFactory = new SocketFactory(),
            ServerKey = serverKey,
            UdpReceiveBufferSize = null,
            UdpSendBufferSize = null
        });

        return clientUdpChannel;
    }

    private static UdpChannel CreateServerUdpChannel(UdpClient udpClient, uint sessionId, byte[] serverKey,
        byte[] sessionKey)
    {
        var serverTransmitter = new ServerUdpChannelTransmitterTest(udpClient, serverKey);
        var serverUdpChannel = new UdpChannel(serverTransmitter,
            new UdpChannelOptions {
                SessionKey = sessionKey,
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 0), // This will be set later
                ChannelId = Guid.CreateVersion7().ToString(),
                Lifespan = null,
                SessionId = sessionId,
                AutoDisposePackets = true,
                Blocking = false,
                ProtocolVersion = 4,
                LeaveTransmitterOpen = false,
            });
        serverTransmitter.UdpChannel = serverUdpChannel;
        return serverUdpChannel;
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

        // create keys
        var serverKey = VhUtils.GenerateKey();
        var sessionKey = VhUtils.GenerateKey();

        // Create server
        using var serverUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint?)serverUdpClient.Client.LocalEndPoint ??
                             throw new Exception("Server connection is not established");
        var serverUdpChannel = CreateServerUdpChannel(serverUdpClient, 1, serverKey: serverKey, sessionKey: sessionKey);
        var serverReceivedPackets = new List<IpPacket>();
        serverUdpChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            // ReSharper disable once AccessToDisposedClosure
            serverReceivedPackets.Add(ipPacket);
            serverUdpChannel.SendPacketQueued(ipPacket.Clone());
        };
        serverUdpChannel.Start();

        // Create client
        var clientUdpChannel =
            CreateClientUdpChannel(serverEndPoint, serverKey: serverKey, sessionKey: sessionKey, sessionId: 1);
        var clientReceivedPackets = new List<IpPacket>();
        clientUdpChannel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
        };
        clientUdpChannel.Start();

        // send packet to server through channel
        foreach (var ipPacket in packets)
            clientUdpChannel.SendPacketQueued(ipPacket.Clone());

        await VhTestUtil.AssertEqualsWait(packets.Count, () => serverReceivedPackets.Count);
        await VhTestUtil.AssertEqualsWait(packets.Count, () => clientReceivedPackets.Count);
    }

    [TestMethod]
    public async Task UdpChannel_via_Tunnel()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger(LogLevel.Trace);

        var waitHandle = new EventWaitHandle(true, EventResetMode.AutoReset);
        waitHandle.Reset();

        // test packets
        var ipPackets = new List<IpPacket> {
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)),
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)),
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true))
        };

        // create keys
        var serverKey = VhUtils.GenerateKey();
        var sessionKey = VhUtils.GenerateKey();

        // Create server
        using var serverUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint?)serverUdpClient.Client.LocalEndPoint ??
                             throw new Exception("Server connection is not established");
        var serverUdpChannel =
            CreateServerUdpChannel(serverUdpClient, sessionId: 1, serverKey: serverKey, sessionKey: sessionKey);
        var serverReceivedPackets = new List<IpPacket>();

        // Create server tunnel
        var serverTunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        serverTunnel.AddChannel(serverUdpChannel);
        serverTunnel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            // ReSharper disable once AccessToDisposedClosure
            serverReceivedPackets.Add(ipPacket);
            serverUdpChannel.SendPacketQueued(ipPacket.Clone());
        };

        // Create client
        var clientUdpChannel = CreateClientUdpChannel(serverEndPoint, serverKey: serverKey,
            sessionId: 1, sessionKey: sessionKey);

        // Create client tunnel
        var clientReceivedPackets = new List<IpPacket>();
        var clientTunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        clientTunnel.AddChannel(clientUdpChannel);
        clientTunnel.PacketReceived += delegate(object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
            waitHandle.Set();
        };

        // send packet to server through tunnel
        foreach (var ipPacket in ipPackets)
            clientTunnel.SendPacketQueued(ipPacket);

        await VhTestUtil.AssertEqualsWait(ipPackets.Count, () => serverReceivedPackets.Count);
        await VhTestUtil.AssertEqualsWait(ipPackets.Count, () => clientReceivedPackets.Count);
    }

    private static async Task SimpleLoopback(TcpListener tcpListener, CancellationToken cancellationToken)
    {
        using var client = await tcpListener.AcceptTcpClientAsync(cancellationToken);

        // Create a memory stream to store the incoming data
        ChunkStream binaryStream = new BinaryStreamStandard(client.GetStream(), Guid.NewGuid().ToString(), true);
        while (true) {
            // echo back the data to the client
            await binaryStream.CopyToAsync(binaryStream, cancellationToken);
            await binaryStream.DisposeAsync();
            if (!binaryStream.CanReuse)
                break;

            binaryStream = await binaryStream.CreateReuse();
        }

        Assert.IsFalse(binaryStream.CanReuse);
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
    public async Task ChunkStream_Binary()
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
        await using var binaryStream = new BinaryStreamStandard(stream, Guid.NewGuid().ToString(), true);

        // check stream by echo
        await CheckStreamEcho(binaryStream, cts.Token);
        await CheckStreamEcho(await binaryStream.CreateReuse(), cts.Token);
        await CheckStreamEcho(await binaryStream.CreateReuse(), cts.Token);
        Assert.IsTrue(binaryStream.CanReuse);

        binaryStream.PreventReuse();
        await Assert.ThrowsAsync<InvalidOperationException>(() => binaryStream.CreateReuse());

        // read data from stream
        await cts.CancelAsync();
        tcpListener.Stop();
    }
}