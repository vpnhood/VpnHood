using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.TcpStack.Abstractions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.TcpStack.Test;

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
        tcpStack.ProcessIncoming(synPacket);

        // Assert - Should receive SYN-ACK
        Assert.HasCount(1, sentPackets, "Should send SYN-ACK");
        var synAckPacket = sentPackets[0];
        var synAckTcp = synAckPacket.ExtractTcp();
        Assert.IsTrue(synAckTcp.Synchronize, "SYN-ACK should have SYN flag");
        Assert.IsTrue(synAckTcp.Acknowledgment, "SYN-ACK should have ACK flag");
        Assert.AreEqual(1001u, synAckTcp.AcknowledgmentNumber, "ACK number should be SYN seq + 1");

        // Act - Send final ACK to complete handshake
        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: synAckTcp.SequenceNumber + 1);
        tcpStack.ProcessIncoming(ackPacket);

        // Assert - Connection should be accepted
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var stream = await acceptTask.WaitAsync(cts.Token);
        Assert.IsNotNull(stream, "Stream should be accepted after handshake");
        await stream.DisposeAsync();
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task TcpHandshake_InvalidFinalAck_ShouldNotAcceptConnection()
    {
        // Arrange
        var tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        tcpStack.OnPacketSend = sentPackets.Add;

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket);
        var synAckTcp = sentPackets[0].ExtractTcp();

        // Act - wrong ACK number must not complete the handshake.
        var badAckPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: synAckTcp.SequenceNumber);
        tcpStack.ProcessIncoming(badAckPacket);
        Assert.AreEqual(2, sentPackets.Count(p => p.ExtractTcp().Synchronize), "a wrong ack keeps drawing the SYN-ACK");

        var wasAccepted = await Task.WhenAny(acceptTask, Task.Delay(200)) == acceptTask;
        Assert.IsFalse(wasAccepted, "Connection must not be accepted until the SYN-ACK is acknowledged.");

        // A later valid ACK should still complete the half-open connection.
        var goodAckPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: synAckTcp.SequenceNumber + 1);
        tcpStack.ProcessIncoming(goodAckPacket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var stream = await acceptTask.WaitAsync(cts.Token);
        await stream.DisposeAsync();
    }

    /// <summary>
    /// A lost third ACK, then a lost first data segment: the second segment acknowledges our SYN-ACK with a
    /// sequence inside the window, so it completes the handshake (RFC 9293 3.10.7.4) and the data path dup-acks the
    /// gap. Requiring RcvNxt drew a SYN-ACK, and from the established peer a challenge ACK, until the peer's RTO.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task TcpHandshake_LostFinalAckAndFirstSegment_LaterSegmentEstablishes()
    {
        using var tcpStack = new LocalTcpStack(NoRtoOptions);
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000));
        uint serverSeq;
        lock (sync) serverSeq = sent.Single().ExtractTcp().SequenceNumber;

        // the third ACK and the first 100 bytes are lost; the second segment arrives
        var first = new byte[100];
        var second = new byte[100];
        RandomNumberGenerator.Fill(first);
        RandomNumberGenerator.Fill(second);
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1101, ackNum: serverSeq + 1, payload: second));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var client = await acceptTask.WaitAsync(cts.Token);
        lock (sync) {
            Assert.AreEqual(1, sent.Count(p => p.ExtractTcp().Synchronize), "no second SYN-ACK");
            var dupAck = sent.Last().ExtractTcp();
            Assert.IsTrue(dupAck is { Acknowledgment: true, Synchronize: false, Payload.Length: 0 }, "a dup ACK for the gap");
            Assert.AreEqual(1001u, dupAck.AcknowledgmentNumber);
        }

        // the peer retransmits the first segment, then the second: both are acked and read in order
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: first));
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1101, ackNum: serverSeq + 1, payload: second));
        var read = new byte[200];
        await client.Stream.ReadExactlyAsync(read, cts.Token);
        CollectionAssert.AreEqual(first.Concat(second).ToArray(), read);
        lock (sync) Assert.AreEqual(1201u, sent.Last().ExtractTcp().AcknowledgmentNumber);
        await client.DisposeAsync();
    }

    /// <summary>The established peer's pure ACK with its sequence ahead of RcvNxt completes the handshake too; a
    /// challenge ACK with the whole window in flight sits at the window's right edge, which Linux takes.</summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task TcpHandshake_LostFinalAck_PureAckAtTheWindowEdgeEstablishes()
    {
        using var tcpStack = new LocalTcpStack(NoRtoOptions);
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000));
        TcpPacket synAck;
        lock (sync) synAck = sent.Single().ExtractTcp();

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001u + synAck.WindowSize, ackNum: synAck.SequenceNumber + 1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var client = await acceptTask.WaitAsync(cts.Token);
        lock (sync) Assert.AreEqual(1, sent.Count(p => p.ExtractTcp().Synchronize), "no second SYN-ACK");
        await client.DisposeAsync();
    }

    /// <summary>A sequence below RcvNxt or past the window's edge does not complete the handshake: the right ack
    /// says the peer is established, so a plain ACK tells it where we are, no SYN-ACK; the handshake stays open
    /// for a valid ACK.</summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task TcpHandshake_AckOutsideTheWindow_DoesNotEstablish()
    {
        using var tcpStack = new LocalTcpStack(NoRtoOptions);
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000));
        TcpPacket synAck;
        lock (sync) synAck = sent.Single().ExtractTcp();

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1000, ackNum: synAck.SequenceNumber + 1));
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1002u + synAck.WindowSize, ackNum: synAck.SequenceNumber + 1));
        Assert.AreEqual(0, tcpStack.Diagnostics.EstablishedConnections, "a sequence outside the window must not complete the handshake");
        lock (sync) {
            Assert.AreEqual(1, sent.Count(p => p.ExtractTcp().Synchronize), "no SYN-ACK for a peer that acknowledged ours");
            Assert.AreEqual(2, sent.Count(p => p.ExtractTcp() is { Synchronize: false, Acknowledgment: true, AcknowledgmentNumber: 1001 }),
                "a plain ACK tells the peer where we are");
        }

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: synAck.SequenceNumber + 1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var client = await acceptTask.WaitAsync(cts.Token);
        await client.DisposeAsync();
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
        tcpStack.ProcessIncoming(synPacket);

        var synAckTcp = sentPackets[0].ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;

        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var stream = await acceptTask.WaitAsync(cts.Token);
        sentPackets.Clear();

        // Act - Send data from client to server
        var testData = "Hello"u8.ToArray(); // "Hello"
        var dataPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket);

        await WaitForCondition(() => sentPackets.Count >= 1, cts.Token);

        // Assert - Should receive data ACK
        Assert.IsGreaterThanOrEqualTo(1, sentPackets.Count, "Should send ACK for data");

        // Read data from stream
        var buffer = new byte[100];
        var bytesRead = await stream.Stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);

        Assert.AreEqual(5, bytesRead, "Should read 5 bytes");

        await stream.DisposeAsync();
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task DataPacketWithoutPsh_ShouldBeAckedImmediately()
    {
        // Arrange
        var tcpStack = new LocalTcpStack();
        var sentPackets = new List<IpPacket>();
        object lockObj = new();
        tcpStack.OnPacketSend = packet => {
            lock (lockObj) sentPackets.Add(packet);
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket);
        var synAckTcp = sentPackets[0].ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;

        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stream = await acceptTask.WaitAsync(cts.Token);
        lock (lockObj) sentPackets.Clear();

        // Act - a single non-PSH segment must still get an ACK without allocating delayed-ACK work.
        var testData = "Hello"u8.ToArray();
        var dataPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket);

        await WaitForCondition(() => {
            lock (lockObj) {
                return sentPackets.Any(p => {
                    var tcp = p.ExtractTcp();
                    return tcp is { Acknowledgment: true, Payload.Length: 0, AcknowledgmentNumber: 1006 };
                });
            }
        }, cts.Token);

        lock (lockObj) {
            Assert.Contains(p => {
                var tcp = p.ExtractTcp();
                return tcp is { Acknowledgment: true, Payload.Length: 0, AcknowledgmentNumber: 1006 };
            }, sentPackets, "A lone non-PSH data segment should be ACKed immediately.");
        }

        await stream.DisposeAsync();
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task FullSizeDataPacketsWithoutPsh_ShouldAckEverySecondSegment()
    {
        // Arrange. DelayedAckTimeout is raised so the maintenance sweep's delayed-ACK flush cannot
        // fire between the two segments — this test asserts pure thinning behavior.
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            DefaultMss = 512,
            MaxMss = 512,
            RetxBufferSize = 1024,
            DelayedAckTimeout = TimeSpan.FromMilliseconds(500)
        });
        var sentPackets = new List<IpPacket>();
        object lockObj = new();
        tcpStack.OnPacketSend = packet => {
            lock (lockObj) sentPackets.Add(packet);
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket);
        var synAckTcp = sentPackets[0].ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;

        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stream = await acceptTask.WaitAsync(cts.Token);
        lock (lockObj) sentPackets.Clear();

        // Act - two full-size non-PSH segments should produce one cumulative ACK, not two ACK packets.
        var segment1 = new byte[512];
        var segment2 = new byte[512];
        RandomNumberGenerator.Fill(segment1);
        RandomNumberGenerator.Fill(segment2);

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1, payload: segment1));
        await Task.Delay(100, cts.Token);

        lock (lockObj) {
            Assert.DoesNotContain(p => p.ExtractTcp().Payload.Length == 0 && p.ExtractTcp().AcknowledgmentNumber == 1513, sentPackets,
                "The first full-size non-PSH segment should be ACK-thinned.");
        }

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1513, ackNum: serverSeq + 1, payload: segment2));

        await WaitForCondition(() => {
            lock (lockObj) {
                return sentPackets.Any(p => {
                    var tcp = p.ExtractTcp();
                    return tcp is { Acknowledgment: true, Payload.Length: 0, AcknowledgmentNumber: 2025 };
                });
            }
        }, cts.Token);

        lock (lockObj) {
            var pureAcks = sentPackets.Count(p => {
                var tcp = p.ExtractTcp();
                return tcp is { Acknowledgment: true, Payload.Length: 0 };
            });
            Assert.AreEqual(1, pureAcks, "Two full-size non-PSH segments should produce one cumulative ACK.");
        }

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
        tcpStack.ProcessIncoming(synPacket);

        var synAckTcp = sentPackets[0].ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;

        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var stream = await acceptTask.WaitAsync(cts.Token);
        sentPackets.Clear();

        // Act - Server writes data
        var responseData = "World"u8.ToArray(); // "World"
        await stream.Stream.WriteAsync(responseData, 0, responseData.Length, cts.Token);

        // Give time for async packet emission
        await Task.Delay(100, cts.Token);

        // Assert - Should send data packet
        Assert.IsGreaterThanOrEqualTo(1, sentPackets.Count, "Should send data packet");
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
        tcpStack.OnPacketSend = packet => {
            lock (lockObj) {
                sentPackets.Add(packet);
            }
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));

        // Start echo server
        _ = Task.Run(async () => {
            await foreach (var stream in listener.AcceptAllAsync()) {
                var buffer = new byte[1024];
                while (true) {
                    var bytesRead = await stream.Stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    await stream.Stream.WriteAsync(buffer, 0, bytesRead);
                }
                break;
            }
        });

        // Complete handshake
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket);

        IpPacket synAckPacket;
        lock (lockObj) { synAckPacket = sentPackets[0]; }
        var synAckTcp = synAckPacket.ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;

        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket);

        await Task.Delay(100); // Wait for accept

        // Act - Send data and check for echo
        lock (lockObj) { sentPackets.Clear(); }

        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var dataPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket);

        // Wait for echo response
        await Task.Delay(500);

        // Assert - Should receive echoed data
        IpPacket[] packetsSnapshot;
        lock (lockObj) {
            packetsSnapshot = [.. sentPackets];
        }

        var dataPackets = packetsSnapshot.Where(p => p.ExtractTcp().Payload.Length > 0).ToList();
        Assert.IsNotEmpty(dataPackets, "Should receive echoed data packet");

        var echoedData = dataPackets[0].ExtractTcp().Payload.ToArray();
        Assert.AreSequenceEqual(testData, echoedData, "Echoed data should match sent data");
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
        tcpStack.OnPacketSend = packet => {
            lock (lockObj) {
                sentPackets.Add(packet);
            }
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Complete handshake
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket);

        IpPacket synAckPacket;
        lock (lockObj) { synAckPacket = sentPackets[0]; }
        var synAckTcp = synAckPacket.ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;

        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = await acceptTask.WaitAsync(cts.Token);

        // Act - Send 1KB of data
        var testData = new byte[1024];
        RandomNumberGenerator.Fill(testData);

        var dataPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket);

        // Read data from stream
        var received = new List<byte>();
        var buffer = new byte[256];

        while (received.Count < testData.Length) {
            var bytesRead = await stream.Stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            if (bytesRead == 0) break;
            received.AddRange(buffer.Take(bytesRead));
        }

        // Assert
        Assert.HasCount(testData.Length, received, "Should receive all data");
        CollectionAssert.AreEqual(testData, received.ToArray(), "Data should match");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Regression test for the QUIC + TcpProxy download collapse (40 -> 3 Mbps).
    /// Encodes the Zero Window Probe forward-progress invariant in
    /// <c>LocalTcpConnection.SendZeroWindowProbe</c>: while the peer's receive window is stuck at zero,
    /// every probe must carry a NEW byte at the send frontier (advancing the sequence number) — never
    /// merely retransmit the oldest unacked byte. A probe that does not advance the sequence number never
    /// elicits a window-update ACK from the peer, so the sender deadlocks. A kernel TCP transport hides this
    /// behind large socket buffers; QUIC's tight per-stream window exposes it as a throughput collapse.
    /// The pre-fix code retransmitted <c>_sndUna</c> (returning 0) once the retx ring held data, so probes
    /// stalled at a single sequence number and the write never drained.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task ZeroWindowProbe_ShouldMakeForwardProgress()
    {
        // Arrange — fast probe interval so several probes fire within the test window.
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            ZeroWindowProbeInterval = TimeSpan.FromMilliseconds(50)
        });
        var sentPackets = new List<IpPacket>();
        object lockObj = new();
        tcpStack.OnPacketSend = packet => {
            lock (lockObj) sentPackets.Add(packet);
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Complete handshake.
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket);

        IpPacket synAckPacket;
        lock (lockObj) { synAckPacket = sentPackets[0]; }
        var serverSeq = synAckPacket.ExtractTcp().SequenceNumber;

        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var stream = await acceptTask.WaitAsync(cts.Token);

        // Slam the peer window shut so the server can only make progress via Zero Window Probes.
        var zeroWinAck = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1, window: 0);
        tcpStack.ProcessIncoming(zeroWinAck);

        lock (lockObj) sentPackets.Clear();

        // Act — server writes a multibyte payload that can ONLY drain one byte per probe.
        var payload = new byte[] { 0xA1, 0xB2, 0xC3, 0xD4 };
        var writeTask = stream.Stream.WriteAsync(payload, 0, payload.Length, cts.Token);

        // The write completes only if probes keep advancing and drain the whole payload (forward progress).
        // The pre-fix bug would stall the write here until the cancellation timeout.
        try {
            await writeTask.WaitAsync(TimeSpan.FromSeconds(2), cts.Token);
        }
        catch (TimeoutException) {
            Assert.Fail("Write stalled under a zero peer window — Zero Window Probes are not making " +
                        "forward progress (the QUIC+TcpProxy download-collapse regression).");
        }

        // Assert — the probes carried *distinct, advancing* sequence numbers covering the whole payload,
        // proving each probe sent a fresh byte rather than retransmitting _sndUna.
        IpPacket[] snapshot;
        lock (lockObj) { snapshot = [.. sentPackets]; }

        var probeSequences = snapshot
            .Where(p => p.ExtractTcp().Payload.Length > 0)
            .Select(p => p.ExtractTcp().SequenceNumber)
            .Distinct()
            .ToList();

        Assert.IsGreaterThanOrEqualTo(2, probeSequences.Count,
            $"Zero Window Probes must advance the sequence number (forward progress), but only " +
            $"{probeSequences.Count} distinct probe sequence number(s) were sent — the sender is stuck " +
            "retransmitting the same byte (regression).");

        // The frontier should have advanced across the full payload (first byte at serverSeq+1).
        Assert.AreEqual(serverSeq + (uint)payload.Length, probeSequences.Max(),
            "Probes should have advanced through the entire payload.");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// At the MaxConnections cap, a new SYN evicts the most idle ("most unused") connection — provided
    /// its idle time meets EvictionMinIdle — instead of being rejected. The evicted connection's stream
    /// reaches EOF (so its consumer's proxy pump unwinds), while a freshly-active connection is protected.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task MaxConnections_Full_EvictsMostIdleConnection()
    {
        // Arrange — cap of 2; anything idle >= 50 ms is evictable.
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            MaxConnections = 2,
            EvictionMinIdle = TimeSpan.FromMilliseconds(50)
        });
        var sentPackets = new List<IpPacket>();
        object lockObj = new();
        tcpStack.OnPacketSend = p => { lock (lockObj) sentPackets.Add(p); };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var accepted = new System.Collections.Concurrent.ConcurrentDictionary<int, ITcpClient>();
        _ = Task.Run(async () => {
            await foreach (var c in listener.AcceptAllAsync())
                accepted[c.RemoteEndPoint.Port] = c;
        });

        // Conn A, then let it sit idle past the threshold.
        DoHandshake(tcpStack, sentPackets, lockObj, clientPort: 1001, clientSeq: 1000);
        await Task.Delay(120);
        // Conn B, freshly active (idle ~0) -> protected. A stays the most idle.
        DoHandshake(tcpStack, sentPackets, lockObj, clientPort: 1002, clientSeq: 2000);

        await WaitForCondition(() => accepted.ContainsKey(1001) && accepted.ContainsKey(1002),
            new CancellationTokenSource(2000).Token, 2000);
        var connA = accepted[1001];
        var connB = accepted[1002];
        lock (lockObj) sentPackets.Clear();

        // Act — Conn C arrives at the cap; A is the most idle eligible victim.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, 1003, ServerIp, ServerPort, syn: true, seq: 3000));

        // Assert — C admitted (SYN-ACK, not RST).
        IpPacket[] snap;
        lock (lockObj) snap = [.. sentPackets];
        var reply = snap.Select(p => p.ExtractTcp()).FirstOrDefault(t => t.DestinationPort == 1003);
        Assert.IsNotNull(reply, "Stack should reply to conn C's SYN.");
        Assert.IsTrue(reply is { Synchronize: true, Acknowledgment: true },
            "Conn C should be admitted with a SYN-ACK (an idle victim exists).");
        Assert.IsFalse(reply.Reset, "Conn C must not be RST when an idle victim exists.");

        // A was evicted -> its stream reads EOF (0).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var buf = new byte[16];
        var nA = await connA.Stream.ReadAsync(buf, cts.Token).AsTask().WaitAsync(cts.Token);
        Assert.AreEqual(0, nA, "Evicted connection A should read EOF.");

        // B is protected -> still open (its read blocks rather than returning EOF).
        var bStillOpen = false;
        try { await connB.Stream.ReadAsync(buf, new CancellationTokenSource(150).Token).AsTask(); }
        catch (OperationCanceledException) { bStillOpen = true; }
        Assert.IsTrue(bStillOpen, "Protected connection B should remain open (read blocks, no EOF).");
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task StopListening_ByEndpoint_CompletesAcceptLoop()
    {
        var tcpStack = new LocalTcpStack();
        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var completed = false;

        var acceptLoop = Task.Run(async () => {
            await foreach (var _ in listener.AcceptAllAsync()) { }
            completed = true;
        });

        Assert.IsTrue(tcpStack.StopListening(new IpEndPointValue(ServerIp, ServerPort)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await acceptLoop.WaitAsync(cts.Token);
        Assert.IsTrue(completed, "StopListening(endpoint) should stop the listener and complete AcceptAllAsync.");
    }

    /// <summary>
    /// At the MaxConnections cap, if every existing connection is too recently active (idle below
    /// EvictionMinIdle), a new SYN is rejected with an RST — the historical behavior — and no connection
    /// is evicted.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public void MaxConnections_Full_AllFresh_RejectsWithRst()
    {
        // Arrange — cap of 2; eviction threshold so high nothing qualifies.
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            MaxConnections = 2,
            EvictionMinIdle = TimeSpan.FromSeconds(30)
        });
        var sentPackets = new List<IpPacket>();
        object lockObj = new();
        tcpStack.OnPacketSend = p => { lock (lockObj) sentPackets.Add(p); };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        _ = Task.Run(async () => { await foreach (var _ in listener.AcceptAllAsync()) { } });

        DoHandshake(tcpStack, sentPackets, lockObj, clientPort: 1001, clientSeq: 1000);
        DoHandshake(tcpStack, sentPackets, lockObj, clientPort: 1002, clientSeq: 2000);
        lock (lockObj) sentPackets.Clear();

        // Act — Conn C at the cap; all existing connections are fresh (idle << 30 s).
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, 1003, ServerIp, ServerPort, syn: true, seq: 3000));

        // Assert — C is RST, not admitted.
        IpPacket[] snap;
        lock (lockObj) snap = [.. sentPackets];
        var reply = snap.Select(p => p.ExtractTcp()).FirstOrDefault(t => t.DestinationPort == 1003);
        Assert.IsNotNull(reply, "Stack should reply to conn C's SYN.");
        Assert.IsTrue(reply.Reset, "Conn C must be RST when no connection is idle enough to evict.");
        Assert.IsFalse(reply.Synchronize, "Conn C must not be admitted.");
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task SendThrows_OneErrorLineNamesIt_AndTheNextSynIsServed()
    {
        var log = LogCapture.Instance;
        var marker = Guid.NewGuid().ToString("N");
        using var stack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        var lockObj = new object();
        var failSends = true;
        stack.OnPacketSend = packet => {
            if (failSends) throw new IOException("adapter down " + marker);
            lock (lockObj) sent.Add(packet);
        };
        var listener = stack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // the SYN-ACK's send throws: the SYN is dropped, its connection with it, and one error line names the exception
        stack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000));
        var lines = log.Lines(marker);
        Assert.HasCount(1, lines);
        Assert.AreEqual(LogLevel.Error, lines[0].Level);
        StringAssert.Contains(lines[0].Message, "ProcessIncoming");
        Assert.AreEqual(0, stack.Diagnostics.ConnectionCount);

        // the adapter works again: the peer's retransmitted SYN establishes as if nothing had happened
        failSends = false;
        DoHandshake(stack, sent, lockObj, ClientPort, 1000);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var client = await acceptTask.WaitAsync(cts.Token);
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task SendThrowsOnEveryPacket_OneLineASecond_CountsTheSuppressedOnes()
    {
        var log = LogCapture.Instance;
        var marker = Guid.NewGuid().ToString("N");
        using var stack = new LocalTcpStack();
        stack.OnPacketSend = _ => throw new IOException("adapter down " + marker);
        stack.Listen(new IpEndPointValue(ServerIp, ServerPort));

        for (var i = 0; i < 5; i++)
            stack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort + i, ServerIp, ServerPort, syn: true, seq: 1000));
        Assert.HasCount(1, log.Lines(marker), "a burst of failures makes one line");

        await Task.Delay(1100);
        stack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort + 5, ServerIp, ServerPort, syn: true, seq: 1000));
        var lines = log.Lines(marker);
        Assert.HasCount(2, lines);
        StringAssert.Contains(lines[1].Message, "4 more since the last line");
    }

    /// <summary>Only an acceptable segment counts as the peer's activity: a connection fed ACKs with a stale ack
    /// number, or with a current ack at a sequence out of the window, times out as idle while one fed current ACKs
    /// stays.</summary>
    [TestMethod]
    [Timeout(20000)]
    public async Task IdleTimeout_IsNotPostponedByUnacceptableSegments()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            IdleTimeout = TimeSpan.FromSeconds(1),
            IdleCheckInterval = TimeSpan.FromMilliseconds(200)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = Task.Run(async () => {
            var clients = new List<ITcpClient>();
            await foreach (var client in listener.AcceptAllAsync()) {
                clients.Add(client);
                if (clients.Count == 3) break;
            }
            return clients;
        });

        // A: stale ack numbers; B: current ACKs; C: current acks at a sequence out of the window
        const int portA = ClientPort, portB = ClientPort + 1, portC = ClientPort + 2;
        DoHandshake(tcpStack, sent, sync, portA, 1000);
        DoHandshake(tcpStack, sent, sync, portB, 1000);
        DoHandshake(tcpStack, sent, sync, portC, 1000);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var clients = await acceptTask.WaitAsync(cts.Token);
        var isnA = SynAckSequence(sent, sync, portA);
        var isnB = SynAckSequence(sent, sync, portB);
        var isnC = SynAckSequence(sent, sync, portC);

        // 2 s of feeding every 50 ms: twice the idle timeout
        for (var i = 0; i < 40; i++) {
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, portA, ServerIp, ServerPort, ack: true, seq: 1001, ackNum: isnA));
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, portB, ServerIp, ServerPort, ack: true, seq: 1001, ackNum: isnB + 1));
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, portC, ServerIp, ServerPort, ack: true, seq: 1001 + 100_000, ackNum: isnC + 1));
            await Task.Delay(50, cts.Token);
        }
        Assert.AreEqual(1, tcpStack.Diagnostics.ConnectionCount, "A and C timed out, B stays");

        // B is the survivor: its data still reaches its app; A's and C's apps read EOF
        var clientB = clients.Single(c => c.RemoteEndPoint.Port == portB);
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, portB, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: isnB + 1, payload: new byte[10]));
        Assert.AreEqual(10, await clientB.Stream.ReadAsync(new byte[10], cts.Token));
        foreach (var client in clients.Where(c => c != clientB))
            Assert.AreEqual(0, await client.Stream.ReadAsync(new byte[10], cts.Token), "an aborted connection's app reads EOF");
        foreach (var client in clients)
            await client.DisposeAsync();
    }

    // The handshake tests' stack: no RTO, so the maintenance sweep never retransmits a SYN-ACK into their counts.
    private static LocalTcpStackOptions NoRtoOptions => new() {
        RetransmitTimeout = TimeSpan.FromSeconds(30),
        RetransmitMaxTimeout = TimeSpan.FromSeconds(30)
    };

    // The server's ISN for a client port: its SYN-ACK's sequence number.
    private static uint SynAckSequence(List<IpPacket> sent, object sync, int clientPort)
    {
        lock (sync)
            return sent.First(p => p.ExtractTcp() is { Synchronize: true } t && t.DestinationPort == clientPort)
                .ExtractTcp().SequenceNumber;
    }

    // Completes a SYN / SYN-ACK / ACK handshake for the given client port against the server endpoint.
    private static void DoHandshake(LocalTcpStack stack, List<IpPacket> sent, object lockObj,
        int clientPort, uint clientSeq)
    {
        stack.ProcessIncoming(CreateTcpPacket(ClientIp, clientPort, ServerIp, ServerPort, syn: true, seq: clientSeq));
        uint serverSeq;
        lock (lockObj) {
            var synAck = sent.Last(p => {
                var t = p.ExtractTcp();
                return t.DestinationPort == clientPort && t is { Synchronize: true, Acknowledgment: true };
            });
            serverSeq = synAck.ExtractTcp().SequenceNumber;
        }
        stack.ProcessIncoming(CreateTcpPacket(ClientIp, clientPort, ServerIp, ServerPort,
            ack: true, seq: clientSeq + 1, ackNum: serverSeq + 1));
    }

    private static async Task<ITcpClient> AcceptConnectionAsync(ITcpListener listener)
    {
        await foreach (var client in listener.AcceptAllAsync()) {
            return client;
        }
        throw new InvalidOperationException("No connection accepted");
    }

    private static IpPacket CreateTcpPacket(
        IPAddress srcIp, int srcPort,
        IPAddress dstIp, int dstPort,
        bool syn = false, bool ack = false, bool fin = false, bool psh = false, bool rst = false,
        uint seq = 0, uint ackNum = 0,
        byte[]? payload = null,
        ushort window = 65535)
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
        tcp.WindowSize = window;

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
        tcpStack.OnPacketSend = packet => {
            lock (lockObj) sentPackets.Add(packet);
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Complete handshake
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket);

        IpPacket synAckPacket;
        lock (lockObj) { synAckPacket = sentPackets[0]; }
        var synAckTcp = synAckPacket.ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;

        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stream = await acceptTask.WaitAsync(cts.Token);
        lock (lockObj) sentPackets.Clear();

        // Act - Send data packet
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var dataPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket);

        // Wait for ACK
        await Task.Delay(50, cts.Token);

        int ackCountAfterFirst;
        lock (lockObj) { ackCountAfterFirst = sentPackets.Count; }
        Assert.IsGreaterThanOrEqualTo(1, ackCountAfterFirst, "Should send ACK for data");

        // Act - Simulate retransmission (same packet again)
        tcpStack.ProcessIncoming(dataPacket);

        // Wait for ACK of retransmit
        await Task.Delay(50, cts.Token);

        int ackCountAfterRetransmit;
        lock (lockObj) { ackCountAfterRetransmit = sentPackets.Count; }
        Assert.IsGreaterThan(ackCountAfterFirst, ackCountAfterRetransmit, "Should ACK retransmitted packet");

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
        tcpStack.OnPacketSend = packet => {
            lock (lockObj) sentPackets.Add(packet);
        };

        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Complete handshake
        var synPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket);

        IpPacket synAckPacket;
        lock (lockObj) { synAckPacket = sentPackets[0]; }
        var synAckTcp = synAckPacket.ExtractTcp();
        var serverSeq = synAckTcp.SequenceNumber;

        var ackPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1);
        tcpStack.ProcessIncoming(ackPacket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await acceptTask.WaitAsync(cts.Token);
        lock (lockObj) sentPackets.Clear();

        // Act - Send out-of-order packet (skipping seq 1001-1005)
        var testData = new byte[] { 0x06, 0x07, 0x08, 0x09, 0x0A };
        var outOfOrderPacket = CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1006, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(outOfOrderPacket);

        await Task.Delay(50, cts.Token);

        // Assert - Should send duplicate ACK for expected sequence
        IpPacket[] packets;
        lock (lockObj) { packets = [.. sentPackets]; }

        Assert.IsGreaterThanOrEqualTo(1, packets.Length, "Should send ACK");
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
        tcpStack.OnPacketSend = packet => {
            lock (lockObj) sentPackets.Add(packet);
        };

        var listener = tcpStack.Listen(new IpEndPointValue(serverIpV6, serverPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // Act - SYN
        var synPacket = CreateTcpPacket(clientIpV6, clientPort, serverIpV6, serverPort, syn: true, seq: 1000);
        tcpStack.ProcessIncoming(synPacket);

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
        tcpStack.ProcessIncoming(ackPacket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stream = await acceptTask.WaitAsync(cts.Token);
        lock (lockObj) sentPackets.Clear();

        // Act - client sends data
        var testData = "Hello over IPv6!"u8.ToArray();
        var dataPacket = CreateTcpPacket(clientIpV6, clientPort, serverIpV6, serverPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData);
        tcpStack.ProcessIncoming(dataPacket);

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
        lock (lockObj) { outgoing = [.. sentPackets]; }
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
        tcpStack.ProcessIncoming(synPacket);

        // Assert - IPv6 RST
        Assert.HasCount(1, sentPackets);
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
