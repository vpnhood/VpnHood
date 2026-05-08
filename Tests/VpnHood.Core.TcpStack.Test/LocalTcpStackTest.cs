using System.Net;
using System.Security.Cryptography;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.TcpStack.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.TcpStack.Test;

[TestClass]
public sealed class LocalTcpStackTest
{
    private static readonly IPAddress ServerIp = IPAddress.Parse("10.0.0.1");
    private static readonly IPAddress ClientIp = IPAddress.Parse("10.0.0.2");
    private const int ServerPort = 8080;
    private const int ClientPort = 54321;

    /// <summary>
    /// Tests TCP handshake (SYN, SYN-ACK, ACK) using the LocalTcpStack
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task TcpHandshake_ShouldComplete()
    {
        // Arrange
        var tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        tcpStack.OnPacketSend = sentPackets.Add;

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Act - Send SYN packet
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket.Buffer.Span);

        // Assert - Should receive SYN-ACK
        Assert.AreEqual(1, sentPackets.Count, "Should send SYN-ACK");
        var synAckPacket = sentPackets[0];
        var synAckTcp = synAckPacket.ExtractTcp();
        Assert.IsTrue(synAckTcp.Synchronize, "SYN-ACK should have SYN flag");
        Assert.IsTrue(synAckTcp.Acknowledgment, "SYN-ACK should have ACK flag");
        Assert.AreEqual(1001u, synAckTcp.AcknowledgmentNumber, "ACK number should be SYN seq + 1");

        // Act - Send final ACK to complete handshake
        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, 
            ack: true, seq: 1001, ackNum: synAckTcp.SequenceNumber + 1);
        tcpStack.ProcessIncoming(ackPacket.Buffer.Span);

        // Assert - Connection should be accepted
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var stream = await acceptTask.WaitAsync(cts.Token);
        Assert.IsNotNull(stream, "Stream should be accepted after handshake");
        await stream.DisposeAsync();
    }

    /// <summary>
    /// Tests data transfer through LocalTcpStack
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task DataTransfer_ShouldSucceed()
    {
        // Arrange
        ITcpStack tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        tcpStack.OnPacketSend = sentPackets.Add;

        var listener = tcpStack.Listen(new IPEndPoint(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Complete handshake
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket.Buffer.Span);
        
        var synAckTcp = sentPackets[0].ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;
        
        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket.Buffer.Span);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var stream = await acceptTask.WaitAsync(cts.Token);
        sentPackets.Clear();

        // Act - Send data from client to server
        var testData = "Hello"u8.ToArray(); // "Hello"
        var dataPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket.Buffer.Span);

        await WaitForCondition(() => sentPackets.Count >= 1, cts.Token);

        // Assert - Should receive data ACK
        Assert.IsTrue(sentPackets.Count >= 1, "Should send ACK for data");
        
        // Read data from stream
        var buffer = new byte[100];
        var bytesRead = await stream.Stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);

        Assert.AreEqual(5, bytesRead, "Should read 5 bytes");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Tests server writing data back to client
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task ServerWrite_ShouldSendPacket()
    {
        // Arrange
        var tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        tcpStack.OnPacketSend = sentPackets.Add;

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Complete handshake
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket.Buffer.Span);
        
        var synAckTcp = sentPackets[0].ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;
        
        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket.Buffer.Span);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var stream = await acceptTask.WaitAsync(cts.Token);
        sentPackets.Clear();

        // Act - Server writes data
        var responseData = "World"u8.ToArray(); // "World"
        await stream.Stream.WriteAsync(responseData, 0, responseData.Length, cts.Token);
        
        // Give time for async packet emission
        await Task.Delay(100, cts.Token);

        // Assert - Should send data packet
        Assert.IsTrue(sentPackets.Count >= 1, "Should send data packet");
        var dataPacket = sentPackets.First(p => p.ExtractTcp().Payload.Length > 0);
        var dataTcp = dataPacket.ExtractTcp();
        
        Assert.IsTrue(dataTcp.Acknowledgment, "Data packet should have ACK flag");
        Assert.IsTrue(dataTcp.Push, "Data packet should have PSH flag");
        CollectionAssert.AreEqual(responseData, dataTcp.Payload.ToArray(), "Payload should match");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Tests echo functionality through the TCP stack
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task EchoTest_ShouldSucceed()
    {
        // Arrange
        var tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        object lockObj = new();
        tcpStack.OnPacketSend = packet =>
        {
            lock (lockObj)
            {
                sentPackets.Add(packet);
            }
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        
        // Start echo server
        _ = Task.Run(async () =>
        {
            await foreach (var stream in listener.AcceptAllAsync())
            {
                var buffer = new byte[1024];
                while (true)
                {
                    var bytesRead = await stream.Stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    await stream.Stream.WriteAsync(buffer, 0, bytesRead);
                }
                break;
            }
        });

        // Complete handshake
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket.Buffer.Span);
        
        IpPacket synAckPacket;
        lock (lockObj) { synAckPacket = sentPackets[0]; }
        var synAckTcp = synAckPacket.ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;
        
        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket.Buffer.Span);
        
        await Task.Delay(100); // Wait for accept

        // Act - Send data and check for echo
        lock (lockObj) { sentPackets.Clear(); }
        
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var dataPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket.Buffer.Span);
        
        // Wait for echo response
        await Task.Delay(500);
        
        // Assert - Should receive echoed data
        IpPacket[] packetsSnapshot;
        lock (lockObj) { packetsSnapshot = sentPackets.ToArray(); }
        
        var dataPackets = packetsSnapshot.Where(p => p.ExtractTcp().Payload.Length > 0).ToList();
        Assert.IsTrue(dataPackets.Count > 0, "Should receive echoed data packet");
        
        var echoedData = dataPackets[0].ExtractTcp().Payload.ToArray();
        CollectionAssert.AreEqual(testData, echoedData, "Echoed data should match sent data");
    }

    /// <summary>
    /// Tests larger data transfer (multiple packets)
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task LargeDataTransfer_ShouldSucceed()
    {
        // Arrange
        var tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        object lockObj = new();
        tcpStack.OnPacketSend = packet =>
        {
            lock (lockObj)
            {
                sentPackets.Add(packet);
            }
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Complete handshake
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket.Buffer.Span);
        
        IpPacket synAckPacket;
        lock (lockObj) { synAckPacket = sentPackets[0]; }
        var synAckTcp = synAckPacket.ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;
        
        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket.Buffer.Span);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = await acceptTask.WaitAsync(cts.Token);

        // Act - Send 1KB of data
        var testData = new byte[1024];
        RandomNumberGenerator.Fill(testData);
        
        var dataPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket.Buffer.Span);

        // Read data from stream
        var received = new List<byte>();
        var buffer = new byte[256];
        
        while (received.Count < testData.Length)
        {
            var bytesRead = await stream.Stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            if (bytesRead == 0) break;
            received.AddRange(buffer.Take(bytesRead));
        }

        // Assert
        Assert.AreEqual(testData.Length, received.Count, "Should receive all data");
        CollectionAssert.AreEqual(testData, received.ToArray(), "Data should match");

        await stream.DisposeAsync();
    }

    private static async Task<ITcpClient> AcceptConnectionAsync(ITcpListener listener)
    {
        await foreach (var client in listener.AcceptAllAsync())
        {
            return client;
        }
        throw new InvalidOperationException("No connection accepted");
    }

    private static IpPacket CreateTcpPacket(
        IPAddress srcIp, int srcPort,
        IPAddress dstIp, int dstPort,
        bool syn = false, bool ack = false, bool fin = false, bool psh = false, bool rst = false,
        uint seq = 0, uint ackNum = 0,
        byte[]? payload = null)
    {
        var packet = PacketBuilder.BuildTcp(
            new IPEndPoint(srcIp, srcPort),
            new IPEndPoint(dstIp, dstPort),
            ReadOnlySpan<byte>.Empty,
            payload ?? ReadOnlySpan<byte>.Empty);
        
        var tcp = packet.ExtractTcp();
        tcp.Synchronize = syn;
        tcp.Acknowledgment = ack;
        tcp.Finish = fin;
        tcp.Push = psh;
        tcp.Reset = rst;
        tcp.SequenceNumber = seq;
        tcp.AcknowledgmentNumber = ackNum;
        tcp.WindowSize = 65535;
        
        packet.UpdateAllChecksums();
        return packet;
    }

    /// <summary>
    /// Tests that retransmitted packets are properly ACKed without duplicating data
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task RetransmittedPacket_ShouldBeAckedWithoutDuplicatingData()
    {
        // Arrange
        var tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        object lockObj = new();
        tcpStack.OnPacketSend = packet =>
        {
            lock (lockObj) sentPackets.Add(packet);
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Complete handshake
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket.Buffer.Span);
        
        IpPacket synAckPacket;
        lock (lockObj) { synAckPacket = sentPackets[0]; }
        var synAckTcp = synAckPacket.ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;
        
        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket.Buffer.Span);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stream = await acceptTask.WaitAsync(cts.Token);
        lock (lockObj) sentPackets.Clear();

        // Act - Send data packet
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var dataPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket.Buffer.Span);
        
        // Wait for ACK
        await Task.Delay(50, cts.Token);
        
        int ackCountAfterFirst;
        lock (lockObj) { ackCountAfterFirst = sentPackets.Count; }
        Assert.IsTrue(ackCountAfterFirst >= 1, "Should send ACK for data");
        
        // Act - Simulate retransmission (same packet again)
        tcpStack.ProcessIncoming(dataPacket.Buffer.Span);
        
        // Wait for ACK of retransmit
        await Task.Delay(50, cts.Token);
        
        int ackCountAfterRetransmit;
        lock (lockObj) { ackCountAfterRetransmit = sentPackets.Count; }
        Assert.IsTrue(ackCountAfterRetransmit > ackCountAfterFirst, "Should ACK retransmitted packet");

        // Read data from stream - should only get 5 bytes, not 10
        var buffer = new byte[100];
        var bytesRead = await stream.Stream.ReadAsync(buffer, cts.Token);

        Assert.AreEqual(testData.Length, bytesRead, "Should receive original data only once");
        CollectionAssert.AreEqual(testData, buffer.Take(bytesRead).ToArray(), "Data should match");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Tests that out-of-order packets trigger duplicate ACKs
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task OutOfOrderPacket_ShouldTriggerDuplicateAck()
    {
        // Arrange
        var tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        object lockObj = new();
        tcpStack.OnPacketSend = packet =>
        {
            lock (lockObj) sentPackets.Add(packet);
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Complete handshake
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket.Buffer.Span);
        
        IpPacket synAckPacket;
        lock (lockObj) { synAckPacket = sentPackets[0]; }
        var synAckTcp = synAckPacket.ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;
        
        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket.Buffer.Span);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await acceptTask.WaitAsync(cts.Token);
        lock (lockObj) sentPackets.Clear();

        // Act - Send out-of-order packet (skipping seq 1001-1005)
        var testData = new byte[] { 0x06, 0x07, 0x08, 0x09, 0x0A };
        var outOfOrderPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1006, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(outOfOrderPacket.Buffer.Span);
        
        await Task.Delay(50, cts.Token);

        // Assert - Should send duplicate ACK for expected sequence
        IpPacket[] packets;
        lock (lockObj) { packets = sentPackets.ToArray(); }
        
        Assert.IsTrue(packets.Length >= 1, "Should send ACK");
        var dupAck = packets.First(p => p.ExtractTcp().Acknowledgment);
        var dupAckTcp = dupAck.ExtractTcp();
        
        // The ACK number should still be for seq 1001 (the expected next seq)
        Assert.AreEqual(1001u, dupAckTcp.AcknowledgmentNumber, 
            "Should ACK the expected sequence (indicating gap)");
    }

    /// <summary>
    /// Tests that the stack handles IPv6 endpoints end-to-end: handshake, data
    /// in both directions, and that the produced packets are IPv6.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task IPv6_HandshakeAndDataTransfer_ShouldSucceed()
    {
        // Arrange
        var serverIpV6 = IPAddress.Parse("fd00::1");
        var clientIpV6 = IPAddress.Parse("fd00::2");
        const int serverPort = 8080;
        const int clientPort = 54321;

        var tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        object lockObj = new();
        tcpStack.OnPacketSend = packet =>
        {
            lock (lockObj) sentPackets.Add(packet);
        };

        var listener = tcpStack.Listen(new IpEndPointValue(serverIpV6, serverPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Act - SYN
        var synPacket = CreateTcpPacket(clientIpV6, clientPort, serverIpV6, serverPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket.Buffer.Span);

        // Assert - SYN-ACK is IPv6
        IpPacket synAckPacket;
        lock (lockObj) { synAckPacket = sentPackets[0]; }
        Assert.AreEqual(IpVersion.IPv6, synAckPacket.Version, "Reply must be IPv6");
        Assert.AreEqual(serverIpV6, synAckPacket.SourceAddress);
        Assert.AreEqual(clientIpV6, synAckPacket.DestinationAddress);
        var synAckTcp = synAckPacket.ExtractTcp();
        Assert.IsTrue(synAckTcp is { Synchronize: true, Acknowledgment: true }, "Should be SYN-ACK");
        Assert.AreEqual(1001u, synAckTcp.AcknowledgmentNumber);
        var serverSeq = synAckTcp.SequenceNumber;

        // Act - final ACK
        var ackPacket = CreateTcpPacket(clientIpV6, clientPort, serverIpV6, serverPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket.Buffer.Span);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stream = await acceptTask.WaitAsync(cts.Token);
        lock (lockObj) sentPackets.Clear();

        // Act - client sends data
        var testData = "Hello over IPv6!"u8.ToArray();
        var dataPacket = CreateTcpPacket(clientIpV6, clientPort, serverIpV6, serverPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket.Buffer.Span);

        var buffer = new byte[100];
        var bytesRead = await stream.Stream.ReadAsync(buffer, cts.Token);
        Assert.AreEqual(testData.Length, bytesRead);
        CollectionAssert.AreEqual(testData, buffer.Take(bytesRead).ToArray());

        // Act - server writes data back
        lock (lockObj) sentPackets.Clear();
        var responseData = "World over IPv6!"u8.ToArray();
        await stream.Stream.WriteAsync(responseData, cts.Token);
        await Task.Delay(100, cts.Token);

        IpPacket[] outgoing;
        lock (lockObj) { outgoing = sentPackets.ToArray(); }
        var serverData = outgoing.First(p => p.ExtractTcp().Payload.Length > 0);
        Assert.AreEqual(IpVersion.IPv6, serverData.Version, "Outgoing data must be IPv6");
        Assert.AreEqual(serverIpV6, serverData.SourceAddress);
        Assert.AreEqual(clientIpV6, serverData.DestinationAddress);
        CollectionAssert.AreEqual(responseData, serverData.ExtractTcp().Payload.ToArray());

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Tests that an unknown IPv6 SYN gets an IPv6 RST back.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public void IPv6_UnknownDestination_ShouldSendIPv6Rst()
    {
        // Arrange
        var serverIpV6 = IPAddress.Parse("fd00::1");
        var clientIpV6 = IPAddress.Parse("fd00::2");

        var tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        tcpStack.OnPacketSend = sentPackets.Add;

        // Act - SYN to a port nobody listens on
        var synPacket = CreateTcpPacket(clientIpV6, 54321, serverIpV6, 9999, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket.Buffer.Span);

        // Assert - IPv6 RST
        Assert.AreEqual(1, sentPackets.Count);
        var rst = sentPackets[0];
        Assert.AreEqual(IpVersion.IPv6, rst.Version);
        Assert.AreEqual(serverIpV6, rst.SourceAddress);
        Assert.AreEqual(clientIpV6, rst.DestinationAddress);
        Assert.IsTrue(rst.ExtractTcp().Reset);
    }

    private static async Task WaitForCondition(Func<bool> condition, CancellationToken ct, int timeoutMs = 1000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(5, ct);
    }
}
