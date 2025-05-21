using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnHood.Core.Client;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Test.Packets;

namespace VpnHood.Test.Tests;

[TestClass]
[SuppressMessage("ReSharper", "DisposeOnUsingVariable")]
public class TunnelTest : TestBase
{
    private class ServerUdpChannelTransmitterTest(UdpClient udpClient, byte[] serverKey, UdpChannel udpChannel)
        : UdpChannelTransmitter(udpClient, serverKey)
    {
        protected override void OnReceiveData(ulong sessionId, IPEndPoint remoteEndPoint, long channelCryptorPosition,
            Span<byte> buffer)
        {
            udpChannel.SetRemote(this, remoteEndPoint);
            udpChannel.OnReceiveData(buffer, channelCryptorPosition);
        }
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
        var serverEndPoint = (IPEndPoint?)serverUdpClient.Client.LocalEndPoint
                             ?? throw new Exception("Server connection is not established");

        var serverUdpChannel = new UdpChannel(
            new UdpChannelOptions {
                SessionId = 1,
                SessionKey = sessionKey,
                AutoDisposePackets = true,
                Blocking = false,
                ProtocolVersion = 4,
                LeaveTransmitterOpen = true
            });

        using var serverUdpChannelTransmitter =
            new ServerUdpChannelTransmitterTest(serverUdpClient, serverKey, serverUdpChannel);
        serverUdpChannel.Start();

        var serverReceivedPackets = new List<IpPacket>();
        serverUdpChannel.PacketReceived += delegate (object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
            serverUdpChannel.SendPacketQueued(ipPacket.Clone());
        };

        // Create client
        var clientUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var clientUdpChannel = new UdpChannel(new UdpChannelOptions {
            SessionId = 1,
            SessionKey = sessionKey,
            AutoDisposePackets = true,
            Blocking = false,
            ProtocolVersion = 4,
            LeaveTransmitterOpen = false
        });

        clientUdpChannel.SetRemote(new ClientUdpChannelTransmitter(clientUdpChannel, clientUdpClient, serverKey),
            serverEndPoint);
        clientUdpChannel.Start();

        var clientReceivedPackets = new List<IpPacket>();
        clientUdpChannel.PacketReceived += delegate (object? _, IpPacket ipPacket) {
            clientReceivedPackets.Add(ipPacket);
        };

        // send packet to server through channel
        foreach (var ipPacket in packets)
            clientUdpChannel.SendPacketQueued(ipPacket.Clone());

        await VhTestUtil.AssertEqualsWait(packets.Count, () => serverReceivedPackets.Count);
        await VhTestUtil.AssertEqualsWait(packets.Count, () => clientReceivedPackets.Count);
    }

    [TestMethod]
    public async Task UdpChannel_via_Tunnel()
    {
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
        var serverEndPoint = (IPEndPoint?)serverUdpClient.Client.LocalEndPoint
                             ?? throw new Exception("Server connection is not established");
        var serverUdpChannel = new UdpChannel(
            new UdpChannelOptions {
                SessionId = 1,
                SessionKey = sessionKey,
                AutoDisposePackets = true,
                Blocking = false,
                ProtocolVersion = 4,
                LeaveTransmitterOpen = true
            });

        using var serverUdpChannelTransmitter =
            new ServerUdpChannelTransmitterTest(serverUdpClient, serverKey, serverUdpChannel);

        var serverReceivedPackets = new List<IpPacket>();
        var serverTunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        serverTunnel.AddChannel(serverUdpChannel);
        serverTunnel.PacketReceived += delegate (object? _, IpPacket ipPacket) {
            serverReceivedPackets.Add(ipPacket);
            serverUdpChannel.SendPacketQueued(ipPacket.Clone());
        };

        // Create client
        var clientUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var clientUdpChannel = new UdpChannel(
            new UdpChannelOptions {
                SessionId = 1,
                SessionKey = sessionKey,
                AutoDisposePackets = true,
                Blocking = false,
                ProtocolVersion = 4,
                LeaveTransmitterOpen = false
            });

        clientUdpChannel.SetRemote(new ClientUdpChannelTransmitter(clientUdpChannel, clientUdpClient, serverKey),
            serverEndPoint);

        var clientReceivedPackets = new List<IpPacket>();
        var clientTunnel = new Tunnel(TestHelper.CreateTunnelOptions());
        clientTunnel.AddChannel(clientUdpChannel);
        clientTunnel.PacketReceived += delegate (object? _, IpPacket ipPacket) {
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
            using var memoryStream = new MemoryStream();
            // Read the request data from the client to memoryStream
            var buffer = new byte[2048];
            var bytesRead = await binaryStream.ReadAsync(buffer, 0, 4, cancellationToken);
            if (bytesRead == 0)
                break;

            if (bytesRead != 4)
                throw new Exception("Unexpected data.");

            var length = BitConverter.ToInt32(buffer, 0);
            var totalRead = 0;
            while (totalRead != length) {
                bytesRead = await binaryStream.ReadAsync(buffer, 0, 120, cancellationToken);
                if (bytesRead == 0) throw new Exception("Unexpected data");
                totalRead += bytesRead;
                memoryStream.Write(buffer, 0, bytesRead);
            }

            await binaryStream.WriteAsync(memoryStream.ToArray(), cancellationToken);

            if (!binaryStream.CanReuse)
                break;

            binaryStream = await binaryStream.CreateReuse();
        }

        Assert.IsFalse(binaryStream.CanReuse);
    }

    [TestMethod]
    public async Task ChunkStream()
    {
        // create server
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        tcpListener.Start();
        var workerTask = SimpleLoopback(tcpListener, cts.Token);

        // connect to server
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync((IPEndPoint)tcpListener.LocalEndpoint, cts.Token);
        var stream = tcpClient.GetStream();

        // send data
        var chunks = new List<string> {
            "HelloHelloHelloHelloHelloHelloHelloHello\r\n",
            "Apple1234,\r\nApple1234,Apple1234,Apple1234,Apple1234,Apple1234,Apple1234,Apple1234,Apple1234,Apple1234",
            "Book009,Book009,Book009,Book009,Book009,Book009,Book009,Book009,Book009,Book009",
            "550Clock\n\r,550Clock,550Clock,550Clock,550Clock,550Clock,550Clock,550Clock,550Clock,550Clock"
        };

        // first stream
        ChunkStream binaryStream = new BinaryStreamStandard(stream, Guid.NewGuid().ToString(), true);
        await binaryStream.WriteAsync(BitConverter.GetBytes(chunks.Sum(x => x.Length)), cts.Token);
        foreach (var chunk in chunks)
            await binaryStream.WriteAsync(Encoding.UTF8.GetBytes(chunk), cts.Token);
        Assert.AreEqual(chunks.Count + 1, binaryStream.WroteChunkCount);

        // read first stream
        var sr = new StreamReader(binaryStream, bufferSize: 10);
        var res = await sr.ReadToEndAsync(cts.Token);
        Assert.AreEqual(string.Join("", chunks), res);

        // write second stream
        binaryStream = await binaryStream.CreateReuse();
        await binaryStream.WriteAsync(BitConverter.GetBytes(chunks.Sum(x => x.Length)), cts.Token);
        foreach (var chunk in chunks)
            await binaryStream.WriteAsync(Encoding.UTF8.GetBytes(chunk), cts.Token);
        Assert.AreEqual(chunks.Count + 1, binaryStream.WroteChunkCount);

        // read second stream
        sr = new StreamReader(binaryStream, bufferSize: 10);
        res = await sr.ReadToEndAsync(cts.Token);
        Assert.AreEqual(string.Join("", chunks), res);

        await binaryStream.DisposeAsync();
        tcpClient.Dispose();

        // task must be completed after binaryStream.DisposeAsync
        await workerTask.WaitAsync(TimeSpan.FromSeconds(2), cts.Token);
        Assert.IsTrue(workerTask.IsCompletedSuccessfully);

        tcpListener.Stop();
        await cts.CancelAsync();
    }

    [TestMethod]
    public async Task ChunkStream_Binary()
    {
        // create server
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        tcpListener.Start();
        _ = SimpleLoopback(tcpListener, cts.Token);

        // connect to server
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync((IPEndPoint)tcpListener.LocalEndpoint, cts.Token);
        var stream = tcpClient.GetStream();

        // send data
        var writeBuffer = new byte[10 * 1024 * 1024 + 2000]; // 10MB buffer size
        var random = new Random();
        random.NextBytes(writeBuffer);

        // write stream
        ChunkStream binaryStream = new BinaryStreamStandard(stream, Guid.NewGuid().ToString(), true);
        await binaryStream.WriteAsync(BitConverter.GetBytes(writeBuffer.Length), cts.Token);
        await binaryStream.WriteAsync((byte[])writeBuffer.Clone(), cts.Token);

        // read stream
        var readBuffer = new byte[writeBuffer.Length];
        await binaryStream.ReadExactlyAsync(readBuffer, cts.Token);
        CollectionAssert.AreEqual(writeBuffer, readBuffer);

        Assert.AreEqual(0, await binaryStream.ReadAsync(readBuffer, cts.Token));
        await binaryStream.DisposeAsync();

        tcpListener.Stop();
        await cts.CancelAsync();
    }
}