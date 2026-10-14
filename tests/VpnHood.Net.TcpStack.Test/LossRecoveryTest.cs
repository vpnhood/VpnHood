using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.TcpStack.Abstractions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.TcpStack.Test;

/// <summary>
/// Regression tests from the 2026-07 TcpStack deep review: loss recovery (RTO / SYN-ACK / FIN
/// retransmission), the retx-ring/sequence integrity of Zero Window Probes, RFC 5961 RST validation,
/// RFC 5681 duplicate-ACK classification, the delayed-ACK flush, hard receive-window enforcement,
/// FIN inside an overlapping retransmit, and the abortive-close RST.
/// </summary>
[TestClass]
public sealed class LossRecoveryTest
{
    private static readonly IPAddress ServerIp = IPAddress.Parse("10.0.0.1");
    private static readonly IPAddress ClientIp = IPAddress.Parse("10.0.0.2");
    private const int ServerPort = 8080;
    private const int ClientPort = 54321;
    public required TestContext TestContext { get; set; }

    /// <summary>
    /// Tail loss: the LAST data segment of a burst produces no duplicate ACKs (nothing arrives after
    /// it), so fast retransmit can never fire. The RTO maintenance sweep must retransmit it. The
    /// pre-fix stack had no retransmission timer at all — the flow hung until the idle timeout.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task TailLoss_RtoRetransmitsUnackedData()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromMilliseconds(300),
            RetransmitMaxTimeout = TimeSpan.FromSeconds(1)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        // Server sends data; the test peer NEVER acks it (the segment was "dropped by the TUN").
        var payload = "tail-loss"u8.ToArray();
        await stream.Stream.WriteAsync(payload);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => {
            lock (sync) {
                return sent.Count(p => {
                    var tcp = p.ExtractTcp();
                    return tcp.Payload.Length > 0 && tcp.SequenceNumber == serverSeq + 1;
                }) >= 2;
            }
        }, cts.Token, 4000);

        lock (sync) {
            var dataPackets = sent.Where(p => p.ExtractTcp().Payload.Length > 0).ToArray();
            Assert.IsTrue(dataPackets.Length >= 2,
                "The unacked tail segment must be retransmitted by the RTO sweep.");
            Assert.IsTrue(dataPackets.All(p => p.ExtractTcp().SequenceNumber == serverSeq + 1),
                "Retransmissions must restart at SndUna.");
            CollectionAssert.AreEqual(payload,
                dataPackets[^1].ExtractTcp().Payload.ToArray()[..payload.Length],
                "The retransmitted bytes must match the original data.");
        }

        await stream.DisposeAsync();
    }

    /// <summary>
    /// A lost FIN previously hung the close until the idle timeout: the FIN was sent exactly once and
    /// is not covered by the retx ring. The RTO sweep must retransmit it.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task LostFin_IsRetransmitted()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromMilliseconds(300),
            RetransmitMaxTimeout = TimeSpan.FromSeconds(1)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        // Graceful close emits a FIN; the test peer never acks it ("dropped by the TUN").
        await stream.DisposeAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => {
            lock (sync) return sent.Count(p => p.ExtractTcp().Finish) >= 2;
        }, cts.Token, 4000);

        lock (sync) {
            var fins = sent.Where(p => p.ExtractTcp().Finish).ToArray();
            Assert.IsTrue(fins.Length >= 2, "An unacknowledged FIN must be retransmitted.");
            Assert.IsTrue(fins.All(p => p.ExtractTcp().SequenceNumber == serverSeq + 1),
                "Every FIN retransmission must reuse the original FIN sequence number.");
        }
    }

    /// <summary>
    /// A lost final handshake ACK previously stranded the connection in SynReceived until the idle
    /// timeout (only a peer SYN retransmit re-triggered a SYN-ACK). The maintenance sweep must
    /// retransmit the SYN-ACK on its own.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task LostFinalHandshakeAck_SynAckIsRetransmitted()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromMilliseconds(300),
            RetransmitMaxTimeout = TimeSpan.FromSeconds(1)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));

        // SYN only — the final ACK never arrives.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => {
            lock (sync) return sent.Count(p => p.ExtractTcp() is { Synchronize: true, Acknowledgment: true }) >= 2;
        }, cts.Token, 4000);

        lock (sync) {
            var synAcks = sent.Where(p => p.ExtractTcp() is { Synchronize: true, Acknowledgment: true }).ToArray();
            Assert.IsTrue(synAcks.Length >= 2, "The SYN-ACK must be retransmitted while the handshake is incomplete.");
            Assert.AreEqual(1, synAcks.Select(p => p.ExtractTcp().SequenceNumber).Distinct().Count(),
                "SYN-ACK retransmissions must be idempotent (same ISN).");
        }
    }

    /// <summary>
    /// A SYN with no final ACK held its connection - its pipe, idle task, stream and client - until the idle
    /// timeout (15 min on desktop and Android, where there is no connection cap), so a local SYN flood was unbounded. A kernel
    /// answers a retransmitted SYN-ACK at once, so once the backoff has reached its cap the silent peer is given
    /// up with a RST, and a later packet on the tuple draws a RST as on any unknown connection.
    /// </summary>
    [TestMethod]
    [Timeout(15000)]
    public async Task HalfOpen_SilentPeers_AreResetOnceTheSynAckBackoffReachesItsCap()
    {
        const int synCount = 100;
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromMilliseconds(100),
            RetransmitMaxTimeout = TimeSpan.FromMilliseconds(800) // 100, 200, 400, 800: four SYN-ACKs, the abort at 1.5 s
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));

        for (var i = 0; i < synCount; i++)
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort + i, ServerIp, ServerPort, syn: true, seq: 1000));
        Assert.AreEqual(synCount, tcpStack.Diagnostics.ConnectionCount);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await WaitForCondition(() => tcpStack.Diagnostics.ConnectionCount == 0, cts.Token, 6000);
        Assert.AreEqual(0, tcpStack.Diagnostics.ConnectionCount, "every half-open connection must be given up");

        lock (sync) {
            foreach (var port in Enumerable.Range(ClientPort, synCount)) {
                var packets = sent.Where(p => p.ExtractTcp().DestinationPort == port).Select(p => p.ExtractTcp()).ToArray();
                Assert.AreEqual(4, packets.Count(t => t is { Synchronize: true, Acknowledgment: true }), $"port {port}: SYN-ACKs");
                Assert.AreEqual(1, packets.Count(t => t.Reset), $"port {port}: one RST");
                var rst = packets[^1];
                Assert.IsTrue(rst.Reset, $"port {port}: the RST is the last packet");
                Assert.IsTrue(rst.Acknowledgment, $"port {port}: the RST carries the ACK a SYN-SENT peer needs");
                Assert.AreEqual(1001u, rst.AcknowledgmentNumber, $"port {port}: ack = the peer's ISN + 1");
                Assert.AreEqual(packets[0].SequenceNumber + 1, rst.SequenceNumber, $"port {port}: seq = our ISN + 1, the peer's RCV.NXT");
            }
            sent.Clear();
        }

        // the tuple is forgotten: a late ACK meets no connection and draws a RST
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, ack: true, seq: 1001, ackNum: 1));
        lock (sync)
            Assert.IsTrue(sent.Single().ExtractTcp().Reset);
    }

    /// <summary>The bound is for a silent peer only: a final ACK that arrives in the last interval, after the final
    /// SYN-ACK and before the abort, establishes the connection.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task HalfOpen_LateFinalAckBeforeTheBound_Establishes()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromMilliseconds(100),
            RetransmitMaxTimeout = TimeSpan.FromMilliseconds(1600) // the fifth SYN-ACK, at 1.5 s, is the last; the abort at 3.1 s
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        // the ACK lands after the final SYN-ACK: the last interval is granted in full
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => {
            lock (sync) return sent.Count(p => p.ExtractTcp() is { Synchronize: true, Acknowledgment: true }) >= 5;
        }, cts.Token, 4000);

        uint serverSeq;
        lock (sync) serverSeq = sent[0].ExtractTcp().SequenceNumber;
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, ack: true, seq: 1001, ackNum: serverSeq + 1));
        var client = await acceptTask.WaitAsync(cts.Token);
        Assert.AreEqual(1, tcpStack.Diagnostics.ConnectionCount);
        lock (sync)
            Assert.IsFalse(sent.Any(p => p.ExtractTcp().Reset), "no RST before the bound");
        await client.DisposeAsync();
    }

    /// <summary>
    /// A full retransmission ring is the RTO's business, not the zero-window probe's: while nothing is
    /// acked the writer waits - no probe consumes a byte the ring cannot store (the 2026-07 corruption), no
    /// segment is retransmitted beside the RTO - and the stream survives intact once the peer resumes.
    /// </summary>
    [TestMethod]
    [Timeout(15000)]
    public async Task RingFull_WriterWaitsWithoutProbing_AndStreamStaysIntact()
    {
        const int retxSize = 600;
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            MaxMss = 536,
            DefaultMss = 536,
            RetransmitBufferSize = retxSize,
            ZeroWindowProbeInterval = TimeSpan.FromMilliseconds(50),
            RetransmitTimeout = TimeSpan.FromSeconds(30), // keep the RTO out of this test
            RetransmitMaxTimeout = TimeSpan.FromSeconds(30)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        // The server writes more than the ring holds; the peer acks nothing, so the ring fills (two
        // segments: 536 + 64) and the writer parks on it.
        var payload = new byte[2000];
        RandomNumberGenerator.Fill(payload);
        var writeTask = stream.Stream.WriteAsync(payload, 0, payload.Length);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await WaitForCondition(() => {
            lock (sync) return sent.Sum(p => p.ExtractTcp().Payload.Length) >= retxSize;
        }, cts.Token, 3000);
        await Task.Delay(400, cts.Token); // eight probe intervals: nothing may be sent in them

        var frontierLimit = serverSeq + 1 + retxSize; // first unsendable seq while nothing is acked
        lock (sync) {
            var dataPackets = sent.Where(p => p.ExtractTcp().Payload.Length > 0).ToArray();
            Assert.AreEqual(2, dataPackets.Length,
                "a ring-limited writer neither probes nor retransmits on its own; the RTO alone recovers");
            foreach (var p in dataPackets) {
                var tcp = p.ExtractTcp();
                var end = tcp.SequenceNumber + (uint)tcp.Payload.Length;
                Assert.IsLessThanOrEqualTo(0, (int)(end - frontierLimit),
                    $"Sequence frontier advanced past the retx ring capacity while nothing was acked " +
                    $"(seq end {end} > limit {frontierLimit}): a probe consumed a byte it cannot back.");
            }
        }

        // Resume acknowledging: cumulatively ack everything sent so far, then keep acknowledging until
        // the write completes; finally verify the stream content is intact (reconstructed by sequence number).
        _ = Task.Run(async () => {
            uint acked = 0;
            while (!writeTask.IsCompleted) {
                uint highest;
                lock (sync) {
                    highest = sent.Where(p => p.ExtractTcp().Payload.Length > 0)
                        .Select(p => p.ExtractTcp().SequenceNumber + (uint)p.ExtractTcp().Payload.Length)
                        .DefaultIfEmpty(serverSeq + 1)
                        .Max();
                }
                if (highest != acked) {
                    acked = highest;
                    tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                        ack: true, seq: 1001, ackNum: highest));
                }
                await Task.Delay(20, TestContext.CancellationToken);
            }
        }, cts.Token);

        await writeTask.WaitAsync(cts.Token);

        // Reconstruct the byte stream from every data packet (dedup by sequence number).
        var reconstructed = new byte[payload.Length];
        var seen = new bool[payload.Length];
        lock (sync) {
            foreach (var p in sent.Where(p => p.ExtractTcp().Payload.Length > 0)) {
                var tcp = p.ExtractTcp();
                var start = (int)(tcp.SequenceNumber - (serverSeq + 1));
                var bytes = tcp.Payload.ToArray();
                for (var i = 0; i < bytes.Length && start + i < payload.Length; i++) {
                    if (start + i < 0) continue;
                    reconstructed[start + i] = bytes[i];
                    seen[start + i] = true;
                }
            }
        }
        Assert.IsTrue(seen.All(s => s), "Every payload byte must have been transmitted at its own sequence number.");
        CollectionAssert.AreEqual(payload, reconstructed,
            "The reconstructed stream must match the written data (no ring/sequence desync).");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Burst loss with a parked writer: the peer loses an ENTIRE in-flight window (a batched TUN write
    /// drop at peak rate) while the writer is parked on a full retx ring with more data pending, then -
    /// like a real kernel - acks whatever arrives in order. The RTO retransmits the first segment and each
    /// partial ACK retransmits the next one at once (RFC 6582), so the write completes promptly and no
    /// segment goes out more than twice - not one lost segment per timer tick (the 2026-07-01 speedtest
    /// "collapse to ~1 then slowly recover" incident).
    /// </summary>
    [TestMethod]
    [Timeout(30000)]
    public async Task BurstLoss_PartialAcks_RecoverAtSegmentGranularity()
    {
        const int retxSize = 2048;
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            MaxMss = 536,
            DefaultMss = 536,
            RetransmitBufferSize = retxSize,
            ZeroWindowProbeInterval = TimeSpan.FromMilliseconds(50),
            RetransmitTimeout = TimeSpan.FromMilliseconds(150),
            RetransmitMaxTimeout = TimeSpan.FromSeconds(1)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        // Write more than the ring holds; the initial ring-filling burst is "dropped by the TUN".
        var payload = new byte[6000];
        RandomNumberGenerator.Fill(payload);
        var writeTask = stream.Stream.WriteAsync(payload, 0, payload.Length);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await WaitForCondition(() => {
            lock (sync) return sent.Sum(p => p.ExtractTcp().Payload.Length) >= retxSize;
        }, cts.Token, 3000);
        await Task.Delay(100, cts.Token); // let the writer park on the full ring

        int lostPackets;
        lock (sync) lostPackets = sent.Count; // everything so far was lost

        // Echo peer: acks each data arrival cumulatively (buffering out-of-order data like a real
        // receiver), starting from a receive position that never saw the initial burst.
        var received = new bool[payload.Length];
        var contiguous = 0;
        var processed = lostPackets;
        var echoToken = cts.Token; // capture the token (a struct): safe even after the cts is disposed
        var echo = Task.Run(async () => {
            while (!echoToken.IsCancellationRequested && !writeTask.IsCompleted) {
                var acks = new List<uint>();
                lock (sync) {
                    for (; processed < sent.Count; processed++) {
                        var tcp = sent[processed].ExtractTcp();
                        if (tcp.Payload.Length == 0) continue;
                        var start = (int)(tcp.SequenceNumber - (serverSeq + 1));
                        for (var i = 0; i < tcp.Payload.Length; i++)
                            if (start + i >= 0 && start + i < received.Length)
                                received[start + i] = true;
                        while (contiguous < received.Length && received[contiguous]) contiguous++;
                        // One ACK per data arrival: a cumulative ACK for in-order data, a duplicate
                        // ACK for an out-of-order segment — exactly what a real kernel emits.
                        acks.Add((uint)(serverSeq + 1 + contiguous));
                    }
                }
                foreach (var ackNum in acks)
                    tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                        ack: true, seq: 1001, ackNum: ackNum));
                await Task.Delay(10, TestContext.CancellationToken);
            }
        }, cts.Token);

        // Partial-ACK recovery completes this in well under a second; one segment per timer tick needs
        // seconds, a 1-byte probe minutes.
        await writeTask.WaitAsync(TimeSpan.FromSeconds(3), cts.Token);
        await echo;

        lock (sync) {
            var mostSentSeq = sent.Where(p => p.ExtractTcp().Payload.Length > 0)
                .GroupBy(p => p.ExtractTcp().SequenceNumber).Max(g => g.Count());
            Assert.IsLessThanOrEqualTo(2, mostSentSeq, "no segment goes out more than twice: one loss, one retransmission");
        }

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Ring-limited and the peer silent: only the RTO retransmits (with backoff); the writer's probe
    /// interval, far shorter than the RTO, must not add retransmissions of the same segment.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task RingLimited_SilentPeer_OnlyTheRtoRetransmits()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            MaxMss = 536,
            DefaultMss = 536,
            RetransmitBufferSize = 1072, // two segments
            ZeroWindowProbeInterval = TimeSpan.FromMilliseconds(50),
            RetransmitTimeout = TimeSpan.FromMilliseconds(300),
            RetransmitMaxTimeout = TimeSpan.FromMilliseconds(600)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        var writeTask = stream.Stream.WriteAsync(new byte[3000]).AsTask(); // fills the ring, then parks
        await Task.Delay(1300); // RTO at 300 ms, backed off to 600 ms: at most 3 retransmissions

        lock (sync) {
            var first = sent.Where(p => p.ExtractTcp().Payload.Length > 0 && p.ExtractTcp().SequenceNumber == serverSeq + 1).Count();
            Assert.IsGreaterThanOrEqualTo(2, first, "the RTO retransmits the first segment");
            Assert.IsLessThanOrEqualTo(4, first, "no probe-interval retransmissions beside the RTO");
        }
        await stream.DisposeAsync();
        try { await writeTask; } catch (IOException) { /* the parked write ends with the stream */ }
    }

    /// <summary>During one recovery, further duplicate ACKs do not retransmit the same segment again.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task DuplicateAcks_DuringRecovery_RetransmitOnce()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromSeconds(5),
            RetransmitMaxTimeout = TimeSpan.FromSeconds(5)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        await stream.Stream.WriteAsync(new byte[5 * 536]); // five segments (default MSS 536)
        for (var i = 0; i < 9; i++)
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                ack: true, seq: 1001, ackNum: serverSeq + 1));

        lock (sync) {
            var retx = sent.Count(p => p.ExtractTcp().Payload.Length > 0 && p.ExtractTcp().SequenceNumber == serverSeq + 1) - 1;
            Assert.AreEqual(1, retx, "nine duplicate ACKs: one fast retransmission, not three");
        }
        await stream.DisposeAsync();
    }

    /// <summary>
    /// A zero window is flow control, not loss: the ACKs of our probes must not start a recovery, or it would
    /// outlive the window and swallow the real duplicate ACKs after it reopens, stalling the flow for an RTO.
    /// </summary>
    [TestMethod]
    [Timeout(15000)]
    public async Task ZeroWindow_ProbeAcksStartNoRecovery_DupAcksAfterReopenRetransmitAtOnce()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            ZeroWindowProbeInterval = TimeSpan.FromMilliseconds(50),
            RetransmitTimeout = TimeSpan.FromMilliseconds(400),
            RetransmitMaxTimeout = TimeSpan.FromSeconds(4)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        // The peer closes its window; the writer probes with one byte at a time, each dropped and acked with
        // window 0 (a real receiver discards out-of-window bytes). The RTO fires in this period too.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1, window: 0));
        var writeTask = stream.Stream.WriteAsync(new byte[2000]).AsTask();
        var probesAcked = 0;
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < 1000) {
            int probes;
            lock (sync) probes = sent.Count(p => p.ExtractTcp().Payload.Length > 0);
            for (; probesAcked < probes; probesAcked++)
                tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                    ack: true, seq: 1001, ackNum: serverSeq + 1, window: 0));
            await Task.Delay(10);
        }
        Assert.IsGreaterThanOrEqualTo(3, probesAcked, "the zero-window period produced probes");

        // The window reopens: new data flows; the peer, still missing everything from serverSeq+1, answers
        // each new segment with a duplicate ACK. The third must retransmit at once.
        lock (sync) sent.Clear();
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1, window: 65535));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => { lock (sync) return sent.Any(p => p.ExtractTcp().Payload.Length > 1); }, cts.Token, 2000);
        lock (sync) sent.Clear();
        for (var i = 0; i < 3; i++)
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                ack: true, seq: 1001, ackNum: serverSeq + 1, window: 65535));

        await WaitForCondition(() => {
            lock (sync) return sent.Any(p => p.ExtractTcp() is { Payload.Length: > 0 } t && t.SequenceNumber == serverSeq + 1);
        }, cts.Token, 100);
        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp() is { Payload.Length: > 0 } t && t.SequenceNumber == serverSeq + 1),
                "three duplicate ACKs after the window reopened must retransmit from SndUna at once");

        await stream.DisposeAsync();
        try { await writeTask; } catch (IOException) { /* the parked write ends with the stream */ }
    }

    /// <summary>A writer parked on a full ring must fail when the peer resets, without anyone reading or disposing.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task RingLimitedWriter_PeerRst_FailsTheWrite()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            MaxMss = 536,
            DefaultMss = 536,
            RetransmitBufferSize = 1072
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        var writeTask = stream.Stream.WriteAsync(new byte[3000]).AsTask(); // fills the ring, then parks
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => { lock (sync) return sent.Sum(p => p.ExtractTcp().Payload.Length) >= 1072; }, cts.Token, 2000);

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, rst: true, seq: 1001));

        await Assert.ThrowsExactlyAsync<IOException>(() => writeTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    /// <summary>Tail and FIN lost together: once the partial ACKs reach the FIN, it is retransmitted at once.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task LostTailAndFin_PartialAckAtTheFin_RetransmitsItAtOnce()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromMilliseconds(300),
            RetransmitMaxTimeout = TimeSpan.FromSeconds(2)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        await stream.Stream.WriteAsync(new byte[2 * 536]); // two segments
        await stream.DisposeAsync(); // FIN at serverSeq + 1073; all three "lost"
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => {
            lock (sync) return sent.Count(p => p.ExtractTcp().SequenceNumber == serverSeq + 1 && p.ExtractTcp().Payload.Length > 0) >= 2;
        }, cts.Token, 2000); // the RTO retransmitted segment 1

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1 + 536)); // partial: segment 2 goes out at once
        lock (sync) sent.Clear();
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1 + 1072)); // partial at the FIN

        await WaitForCondition(() => { lock (sync) return sent.Any(p => p.ExtractTcp().Finish); }, cts.Token, 100);
        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Finish), "the FIN is retransmitted on the partial ACK, not after the RTO");
    }

    /// <summary>A partial ACK during recovery retransmits the next lost segment at once, not after another RTO.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task PartialAck_RetransmitsTheNextLostSegmentAtOnce()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromMilliseconds(300),
            RetransmitMaxTimeout = TimeSpan.FromSeconds(2)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        await stream.Stream.WriteAsync(new byte[3 * 536]); // three segments, all "lost"
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => {
            lock (sync) return sent.Count(p => p.ExtractTcp().SequenceNumber == serverSeq + 1 && p.ExtractTcp().Payload.Length > 0) >= 2;
        }, cts.Token, 2000); // the RTO retransmitted segment 1
        lock (sync) sent.Clear();

        // The peer got the retransmitted segment 1 and acks it: a partial ACK (segments 2 and 3 still lost).
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1 + 536));

        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().SequenceNumber == serverSeq + 1 + 536 && p.ExtractTcp().Payload.Length > 0),
                "the partial ACK retransmits segment 2 at once");
        await stream.DisposeAsync();
    }

    /// <summary>
    /// RFC 5961: an out-of-window RST must be ignored, an in-window (but inexact) RST must only elicit
    /// a challenge ACK, and only an exact-match RST closes the connection. The pre-fix stack accepted
    /// any RST, allowing blind resets.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task Rst_SequenceValidation_BlocksBlindReset()
    {
        var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // 1) Far out-of-window RST: ignored, connection stays alive.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            rst: true, seq: 1001 + 70000));

        // 2) In-window but inexact RST: challenge ACK, connection stays alive.
        lock (sync) sent.Clear();
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            rst: true, seq: 1001 + 10));
        await WaitForCondition(() => {
            lock (sync) return sent.Any(p => p.ExtractTcp() is { Acknowledgment: true, Reset: false });
        }, cts.Token);
        lock (sync) {
            Assert.Contains(p => p.ExtractTcp() is { Acknowledgment: true, Reset: false }, sent,
                "An in-window inexact RST must elicit a challenge ACK.");
        }

        // Prove the connection survived both: data still round-trips.
        var testData = "still-alive"u8.ToArray();
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: testData));
        var buffer = new byte[64];
        var read = await stream.Stream.ReadAsync(buffer, cts.Token);
        Assert.AreEqual(testData.Length, read, "Connection must survive out-of-window and inexact RSTs.");

        // 3) Exact-match RST (rcvNxt advanced by the data): closes the connection -> stream EOF.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            rst: true, seq: 1001 + (uint)testData.Length));
        read = await stream.Stream.ReadAsync(buffer, cts.Token);
        Assert.AreEqual(0, read, "An exact-match RST must close the connection (EOF).");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// RFC 5681: pure window updates (same ack number, CHANGED window) are not duplicate ACKs and must
    /// not trigger fast retransmit; three identical dup-ACKs (unchanged window) must.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task WindowUpdates_AreNotDupAcks_ButRealDupAcksRetransmit()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromSeconds(30), // keep the RTO out of this test
            RetransmitMaxTimeout = TimeSpan.FromSeconds(30)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        // Put unacked data in flight so the dup-ACK machinery is armed.
        await stream.Stream.WriteAsync("dup-ack-probe"u8.ToArray());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => { lock (sync) return sent.Any(p => p.ExtractTcp().Payload.Length > 0); },
            cts.Token);
        lock (sync) sent.Clear();

        // 3 pure WINDOW UPDATES: same ack number, growing window -> must NOT fast-retransmit.
        foreach (var window in new ushort[] { 10000, 20000, 30000 })
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                ack: true, seq: 1001, ackNum: serverSeq + 1, window: window));
        await Task.Delay(300, cts.Token);
        lock (sync) {
            Assert.IsFalse(sent.Any(p => p.ExtractTcp().Payload.Length > 0),
                "Pure window updates must not be classified as duplicate ACKs (spurious fast retransmit).");
        }

        // 3 REAL dup-ACKs: same ack number, same window -> fast retransmit from SndUna.
        for (var i = 0; i < 3; i++)
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                ack: true, seq: 1001, ackNum: serverSeq + 1, window: 30000));
        await WaitForCondition(() => { lock (sync) return sent.Any(p => p.ExtractTcp().Payload.Length > 0); },
            cts.Token, 2000);
        lock (sync) {
            var retx = sent.FirstOrDefault(p => p.ExtractTcp().Payload.Length > 0);
            Assert.IsNotNull(retx, "Three identical duplicate ACKs must trigger fast retransmit.");
            Assert.AreEqual(serverSeq + 1, retx.ExtractTcp().SequenceNumber,
                "Fast retransmit must resend from SndUna.");
        }

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Delayed-ACK flush: an odd trailing full-size non-PSH segment is ACK-thinned at first, but the
    /// maintenance sweep must flush the pending ACK within ~DelayedAckTimeout instead of leaving the
    /// peer to hit its RTO (the pre-fix behavior).
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task DelayedAck_TrailingFullSizeSegment_IsFlushed()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            DefaultMss = 512,
            MaxMss = 512,
            RetransmitBufferSize = 1024,
            DelayedAckTimeout = TimeSpan.FromMilliseconds(200)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        // ONE full-size non-PSH segment: thinned (no immediate ACK) ...
        var segment = new byte[512];
        RandomNumberGenerator.Fill(segment);
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1, payload: segment));

        // ... but the maintenance sweep must flush the pending ACK within DelayedAckTimeout + sweep tick.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => {
            lock (sync) {
                return sent.Any(p => {
                    var tcp = p.ExtractTcp();
                    return tcp is { Acknowledgment: true, Payload.Length: 0, AcknowledgmentNumber: 1001 + 512 };
                });
            }
        }, cts.Token, 2000);

        lock (sync) {
            Assert.Contains(p => {
                var tcp = p.ExtractTcp();
                return tcp is { Acknowledgment: true, Payload.Length: 0, AcknowledgmentNumber: 1001 + 512 };
            }, sent, "A thinned trailing segment must get its ACK flushed by the delayed-ACK timer.");
        }

        await stream.DisposeAsync();
    }

    /// <summary>
    /// The app's drain is the one path that reopens a closed window (the window sweep went with the global budget):
    /// with the pipe full and the peer silent, reading to the 4 KB floor sends exactly one window update.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task ClosedWindow_ReopensOnTheAppDrainAlone_OneUpdateAtTheFloor()
    {
        const int windowSize = 0xFFFF;
        using var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        // the peer fills the window, the app reads nothing: the last ACK advertises 0
        var payload = new byte[1460];
        uint seq = 1001;
        var remaining = windowSize;
        while (remaining > 0) {
            var len = Math.Min(payload.Length, remaining);
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                ack: true, seq: seq, ackNum: serverSeq + 1, payload: payload[..len]));
            seq += (uint)len;
            remaining -= len;
        }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => {
            lock (sync) return sent.Any(p => p.ExtractTcp() is { Acknowledgment: true, WindowSize: 0 });
        }, cts.Token, 2000);
        lock (sync) {
            Assert.AreEqual(0, sent.Last().ExtractTcp().WindowSize, "the full pipe advertises a zero window");
            sent.Clear();
        }

        // the app reads to the floor: one pure ACK carries the reopened window and nothing else goes out
        await client.Stream.ReadExactlyAsync(new byte[4096], cts.Token);
        await WaitForCondition(() => { lock (sync) return sent.Count > 0; }, cts.Token);
        await Task.Delay(200, cts.Token);
        lock (sync) {
            Assert.HasCount(1, sent, "exactly one window update");
            var update = sent[0].ExtractTcp();
            Assert.IsTrue(update is { Acknowledgment: true, Payload.Length: 0 }, "a pure ACK");
            Assert.AreEqual(4096, update.WindowSize);
        }
        await client.DisposeAsync();
    }

    /// <summary>A window under the 4 KB floor reopens on the full drain only: a read that leaves it short of the
    /// floor and under the update step sends nothing.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task SmallWindow_ReopensOnTheFullDrainOnly()
    {
        const int windowSize = 2048;
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions { ReceiveWindowSize = windowSize });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        for (var i = 0; i < 2; i++)
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                ack: true, seq: (uint)(1001 + i * 1024), ackNum: serverSeq + 1, payload: new byte[1024]));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => {
            lock (sync) return sent.Any(p => p.ExtractTcp() is { Acknowledgment: true, WindowSize: 0 });
        }, cts.Token, 2000);
        lock (sync) sent.Clear();

        // 512 read: the window is 512, under the 536-byte update step and the floor (the whole window): nothing
        await client.Stream.ReadExactlyAsync(new byte[512], cts.Token);
        await Task.Delay(200, cts.Token);
        lock (sync) Assert.IsEmpty(sent, "a window short of the floor sends no update");

        // the rest: the window is whole again, one update
        await client.Stream.ReadExactlyAsync(new byte[windowSize - 512], cts.Token);
        await WaitForCondition(() => { lock (sync) return sent.Count > 0; }, cts.Token);
        await Task.Delay(200, cts.Token);
        lock (sync) {
            Assert.HasCount(1, sent, "exactly one window update");
            Assert.AreEqual(windowSize, sent[0].ExtractTcp().WindowSize);
        }
        await client.DisposeAsync();
    }

    /// <summary>
    /// Hard receive-window enforcement: a peer that ignores our advertised window must not grow the
    /// reassembly pipe past ReceiveWindowSize (pre-fix: unbounded memory against a hostile sender).
    /// The overflow is dropped, ACKs stall at the window edge, and the data flows once the app drains.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task ReceiveWindow_IsHardEnforced_AgainstWindowIgnoringPeer()
    {
        const int windowSize = 4096;
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions { ReceiveWindowSize = windowSize });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        // Blast 3 x windowSize in-order WITHOUT the app reading: a window-ignoring peer.
        var blast = new byte[windowSize];
        for (var i = 0; i < 3; i++) {
            RandomNumberGenerator.Fill(blast);
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                ack: true, psh: true, seq: (uint)(1001 + i * windowSize), ackNum: serverSeq + 1, payload: blast));
        }

        Assert.IsTrue(tcpStack.Diagnostics.TotalPipeBufferedBytes <= windowSize,
            $"Unread pipe backlog ({tcpStack.Diagnostics.TotalPipeBufferedBytes}) must never exceed " +
            $"ReceiveWindowSize ({windowSize}) even against a window-ignoring sender.");

        // Drain everything the stack accepted; it must be exactly the first windowSize bytes.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var total = 0;
        var buffer = new byte[1024];
        while (total < windowSize) {
            var n = await stream.Stream.ReadAsync(buffer, cts.Token);
            if (n == 0) break;
            total += n;
        }
        Assert.AreEqual(windowSize, total, "Exactly one window of data must have been accepted.");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// A FIN arriving inside a partially-overlapping retransmitted segment must be processed (pre-fix:
    /// silently ignored; the close waited for a pure-FIN retransmit from the peer).
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task Fin_InsideOverlappingRetransmit_IsProcessed()
    {
        var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // In-order data, fully processed.
        var data = "hello"u8.ToArray();
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: data));

        // The peer retransmits the SAME segment, now with FIN (e.g. close raced the lost ACK).
        lock (sync) sent.Clear();
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, fin: true, seq: 1001, ackNum: serverSeq + 1, payload: data));

        // The FIN must be consumed: ACK number covers data + FIN.
        await WaitForCondition(() => {
            lock (sync) return sent.Any(p => p.ExtractTcp().AcknowledgmentNumber == 1001 + (uint)data.Length + 1);
        }, cts.Token, 2000);
        lock (sync) {
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().AcknowledgmentNumber == 1001 + (uint)data.Length + 1),
                "The FIN inside the overlapping retransmit must be consumed (ACK = data end + 1).");
        }

        // And the stream must deliver the data exactly once, then EOF.
        var buffer = new byte[64];
        var read = await stream.Stream.ReadAsync(buffer, cts.Token);
        Assert.AreEqual(data.Length, read);
        read = await stream.Stream.ReadAsync(buffer, cts.Token);
        Assert.AreEqual(0, read, "After the FIN the stream must reach EOF.");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Abortive close (idle timeout) must notify the peer with an RST (pre-fix: the peer learned nothing
    /// and kept the flow half-open until its own timers gave up).
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task IdleTimeout_AbortsWithRst()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            IdleTimeout = TimeSpan.FromMilliseconds(300),
            IdleCheckInterval = TimeSpan.FromMilliseconds(100)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, _) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => { lock (sync) return sent.Any(p => p.ExtractTcp().Reset); },
            cts.Token, 3000);

        lock (sync) {
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Reset),
                "An idle-timeout close must send a RST so the peer tears down too.");
        }

        // And the local app observes EOF.
        var buffer = new byte[16];
        var read = await stream.Stream.ReadAsync(buffer, cts.Token);
        Assert.AreEqual(0, read, "The idle-aborted stream must read EOF.");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// A graceful close racing a window-blocked write must never emit data at sequence numbers beyond
    /// the FIN (pre-fix: the burst loop did not recheck, so post-FIN segments were emitted and the peer
    /// discarded them).
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task GracefulClose_DuringBlockedWrite_NeverSendsDataAfterFin()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            ZeroWindowProbeInterval = TimeSpan.FromSeconds(30) // suppress probes; keep the writer parked
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        // Slam the peer window shut, then start a write operation that parks on the window signal.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1, window: 0));
        lock (sync) sent.Clear();

        var writeTask = stream.Stream.WriteAsync("blocked-write"u8.ToArray()).AsTask();
        await Task.Delay(100); // let the writer park

        // Dispose (graceful close) while the write is parked, then reopen the window.
        await stream.DisposeAsync();
        await writeTask; // swallowed by the stream's dispose semantics
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1, window: 65535));
        await Task.Delay(200);

        lock (sync) {
            var fin = sent.FirstOrDefault(p => p.ExtractTcp().Finish);
            Assert.IsNotNull(fin, "Graceful close must send a FIN.");
            var finSeq = fin.ExtractTcp().SequenceNumber;
            foreach (var p in sent) {
                var tcp = p.ExtractTcp();
                if (tcp.Payload.Length == 0) continue;
                Assert.IsTrue((int)(tcp.SequenceNumber - finSeq) < 0,
                    $"No data may be emitted at/after the FIN sequence ({tcp.SequenceNumber} >= {finSeq}).");
            }
        }
    }

    /// <summary>
    /// The proxy disposed the stream (its tunnel side ended) but the local app keeps uploading. Nobody can read
    /// those bytes, so they must be answered with a RST and the connection reclaimed at once - not buffered into
    /// a dead pipe whose zero-window probes keep the connection "active" forever.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task StreamDisposed_PeerKeepsSending_IsResetAndReclaimed()
    {
        using var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        await client.DisposeAsync();
        lock (sync) {
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Finish), "a clean close sends a FIN first");
            sent.Clear();
        }

        // The app's ACK of our FIN plus new data.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 2, payload: new byte[1024]));

        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Reset), "data after the app's close must be reset");
        Assert.AreEqual(0, tcpStack.Diagnostics.ConnectionCount, "the connection must be reclaimed at once");
    }

    /// <summary>Bytes the app never read are gone with its stream: the close is a RST, like a kernel's.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task StreamDisposed_WithUnreadData_ResetsAtOnce()
    {
        using var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: new byte[100]));
        lock (sync) sent.Clear();

        await client.DisposeAsync();

        lock (sync) {
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Reset), "unread data at close must reset the connection");
            Assert.IsFalse(sent.Any(p => p.ExtractTcp().Finish), "no FIN beside the RST");
        }
        Assert.AreEqual(0, tcpStack.Diagnostics.ConnectionCount);
    }

    /// <summary>
    /// DropAllConnections (network change, after an ad) must abort: a RST to the local app, EOF to a parked
    /// read, an error to the writer, and the slot freed - not a silent dispose that black-holes the app's socket.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task DropAllConnections_SendsRstAndReleasesStreams()
    {
        using var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, _) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        var readTask = client.Stream.ReadAsync(new byte[16]).AsTask();

        tcpStack.DropAllConnections();

        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Reset), "the local app must be told with a RST");
        Assert.AreEqual(0, tcpStack.Diagnostics.ConnectionCount);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.AreEqual(0, await readTask.WaitAsync(cts.Token), "a parked read ends with EOF");
        await Assert.ThrowsExactlyAsync<IOException>(async () => await client.Stream.WriteAsync(new byte[8]));
    }

    /// <summary>
    /// Passive close: the peer's FIN came first, then the app disposed. Our FIN must be retransmitted while it is
    /// unacknowledged (LastAck), and the peer's final ACK ends the connection without a RST.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task PassiveClose_LostFin_IsRetransmitted_AndFinalAckClosesWithoutRst()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromMilliseconds(200),
            RetransmitMaxTimeout = TimeSpan.FromMilliseconds(400)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, fin: true, seq: 1001, ackNum: serverSeq + 1));
        Assert.AreEqual(0, await client.Stream.ReadAsync(new byte[8]), "the app sees EOF");
        lock (sync) sent.Clear();

        await client.DisposeAsync(); // our FIN; the peer's ACK is "lost"
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => { lock (sync) return sent.Count(p => p.ExtractTcp().Finish) >= 2; }, cts.Token, 2000);
        lock (sync)
            Assert.IsGreaterThanOrEqualTo(2, sent.Count(p => p.ExtractTcp().Finish), "an unacknowledged FIN is retransmitted");
        Assert.AreEqual(1, tcpStack.Diagnostics.ConnectionCount, "the connection waits for the ACK of its FIN");

        lock (sync) sent.Clear();
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1002, ackNum: serverSeq + 2));
        Assert.AreEqual(0, tcpStack.Diagnostics.ConnectionCount, "the final ACK ends the connection");
        lock (sync)
            Assert.IsFalse(sent.Any(p => p.ExtractTcp().Reset), "the final ACK is not answered with a RST");
    }

    /// <summary>
    /// An orphan (the app closed its stream) whose peer only answers our retransmits with window-0 ACKs makes no
    /// progress: those ACKs must not count as activity, or the slot is pinned until the peer's app resumes.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task Orphan_PeerAcksWithoutProgress_IsReclaimedByIdleTimeout()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            IdleTimeout = TimeSpan.FromMilliseconds(400),
            IdleCheckInterval = TimeSpan.FromMilliseconds(100)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        await client.Stream.WriteAsync(new byte[100]); // never acknowledged
        await client.DisposeAsync();

        // The peer's app is suspended: every 100 ms an ACK that acknowledges nothing, window 0.
        for (var i = 0; i < 15 && tcpStack.Diagnostics.ConnectionCount > 0; i++) {
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                ack: true, seq: 1001, ackNum: serverSeq + 1, window: 0));
            await Task.Delay(100);
        }

        Assert.AreEqual(0, tcpStack.Diagnostics.ConnectionCount, "an orphan making no progress is reclaimed");
        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Reset), "the peer is told with a RST");
    }

    /// <summary>
    /// Data after the app's close while our own tail is unacknowledged is dropped, not reset: a RST would make the
    /// peer discard that tail unread. Once everything is acknowledged the peer's retransmit draws the RST.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task DataAfterClose_WithTailInFlight_IsResetOnlyAfterTheTailIsAcked()
    {
        using var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        await client.Stream.WriteAsync(new byte[100]); // the tail: [serverSeq+1, serverSeq+101)
        await client.DisposeAsync(); // FIN at serverSeq+101
        lock (sync) sent.Clear();

        // The peer's next request arrives before it has acknowledged the tail.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: new byte[50]));
        lock (sync)
            Assert.IsFalse(sent.Any(p => p.ExtractTcp().Reset), "no RST while the tail is in flight");
        Assert.AreEqual(1, tcpStack.Diagnostics.ConnectionCount);

        // The peer acknowledges tail and FIN and retransmits its request.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 102, payload: new byte[50]));
        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Reset), "the retransmit after the ACK draws the RST");
        Assert.AreEqual(0, tcpStack.Diagnostics.ConnectionCount);
    }

    /// <summary>
    /// Unread bytes plus our tail in flight at the app's close: the FIN goes first; once the peer acknowledges
    /// tail and FIN - sending nothing more - the deferred reset must still go out, and the pipe total be released.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task CloseWithUnreadData_TailInFlight_ResetsOnceTheTailIsAcked()
    {
        using var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: new byte[1000])); // never read
        await client.Stream.WriteAsync(new byte[100]); // the tail, unacknowledged
        lock (sync) sent.Clear();
        await client.DisposeAsync();
        lock (sync) {
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Finish), "the FIN goes first");
            Assert.IsFalse(sent.Any(p => p.ExtractTcp().Reset), "no RST while the tail is in flight");
        }
        Assert.AreEqual(0, tcpStack.Diagnostics.TotalPipeBufferedBytes, "the unread bytes leave the pipe total at the close");

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 2001, ackNum: serverSeq + 102));
        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Reset), "the unread bytes are reset once all is acked");
        Assert.AreEqual(0, tcpStack.Diagnostics.ConnectionCount);
        Assert.AreEqual(0, tcpStack.Diagnostics.TotalPipeBufferedBytes, "the pipe total is released");
    }

    /// <summary>
    /// An abort's RST must carry SndUna, the peer's RCV.NXT: after a zero-window probe whose byte the peer
    /// rejected, a RST at SndNxt is outside its zero window and silently dropped.
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task Abort_AfterRejectedProbeByte_SendsRstAtSndUna()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            ZeroWindowProbeInterval = TimeSpan.FromMilliseconds(50)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        // The peer closes its window; our write parks and probes with one byte, which the peer rejects (no ACK).
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1, window: 0));
        var writeTask = client.Stream.WriteAsync(new byte[10]).AsTask();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => { lock (sync) return sent.Any(p => p.ExtractTcp().Payload.Length == 1); }, cts.Token, 2000);
        lock (sync) sent.Clear();

        tcpStack.DropAllConnections();

        lock (sync) {
            var rst = sent.Select(p => p.ExtractTcp()).FirstOrDefault(t => t.Reset);
            Assert.IsNotNull(rst, "the abort sends a RST");
            Assert.AreEqual(serverSeq + 1, rst.SequenceNumber, "the RST sits at SndUna, the peer's RCV.NXT");
        }
        await Assert.ThrowsExactlyAsync<IOException>(() => writeTask);
    }

    /// <summary>A retransmitted peer FIN that completes the exchange is still acknowledged after the close.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task LastAck_RetransmittedPeerFinWithOurAck_IsAcknowledged()
    {
        using var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        await client.DisposeAsync(); // our FIN at serverSeq + 1
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, fin: true, seq: 1001, ackNum: serverSeq + 1)); // its FIN crosses ours
        lock (sync) sent.Clear();

        // Our ACK and its ACK were lost: the peer retransmits its FIN, now acknowledging ours.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, fin: true, seq: 1001, ackNum: serverSeq + 2));

        Assert.AreEqual(0, tcpStack.Diagnostics.ConnectionCount, "the exchange is complete");
        lock (sync) {
            Assert.IsTrue(sent.Any(p => p.ExtractTcp() is { Acknowledgment: true, Reset: false, AcknowledgmentNumber: 1002 }),
                "the retransmitted FIN is acknowledged");
            Assert.IsFalse(sent.Any(p => p.ExtractTcp().Reset), "no RST");
        }
    }

    /// <summary>An orphan fed by no-progress ACKs must be evictable for a new connection at the cap.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task Orphan_PeerAcksWithoutProgress_IsEvictedForANewConnection()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            MaxConnections = 1,
            EvictionMinIdle = TimeSpan.FromMilliseconds(300),
            IdleTimeout = TimeSpan.FromSeconds(5),
            IdleCheckInterval = TimeSpan.FromMilliseconds(100)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        await client.Stream.WriteAsync(new byte[100]); // never acknowledged
        await client.DisposeAsync();
        for (var i = 0; i < 6; i++) {
            tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                ack: true, seq: 1001, ackNum: serverSeq + 1, window: 0));
            await Task.Delay(100);
        }
        lock (sync) sent.Clear();

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort + 1, ServerIp, ServerPort, syn: true, seq: 5000));

        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp() is { Synchronize: true, Acknowledgment: true, DestinationPort: ClientPort + 1 }),
                "the orphan is evicted and the new connection admitted");
    }

    /// <summary>Simultaneous close: the peer's FIN arrives before it has acknowledged ours; we wait for that ACK.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task SimultaneousClose_WaitsForTheAckOfOurFin()
    {
        using var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            RetransmitTimeout = TimeSpan.FromMilliseconds(200),
            RetransmitMaxTimeout = TimeSpan.FromMilliseconds(400)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        await client.DisposeAsync(); // our FIN at serverSeq + 1
        // The peer's FIN crosses ours: it acknowledges only our data, not the FIN.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, fin: true, seq: 1001, ackNum: serverSeq + 1));
        Assert.AreEqual(1, tcpStack.Diagnostics.ConnectionCount, "our FIN is still unacknowledged");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitForCondition(() => { lock (sync) return sent.Count(p => p.ExtractTcp().Finish) >= 2; }, cts.Token, 2000);
        lock (sync)
            Assert.IsGreaterThanOrEqualTo(2, sent.Count(p => p.ExtractTcp().Finish), "our FIN is retransmitted");

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1002, ackNum: serverSeq + 2));
        Assert.AreEqual(0, tcpStack.Diagnostics.ConnectionCount, "the ACK of our FIN ends the connection");
    }

    /// <summary>
    /// A zero window on an ACK whose ack number is stale (below SndUna) is not the peer's window: it is ignored, so
    /// a packet that knows only the 4-tuple cannot freeze the sender (RFC 9293 3.10.7.4 takes the window from an
    /// acceptable ACK only).
    /// </summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task StaleAckWithZeroWindow_DoesNotFreezeTheSender()
    {
        using var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);

        // 1000 bytes out and acked
        await client.Stream.WriteAsync(new byte[1000]);
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1 + 1000));

        // a zero window with the handshake's ack number: stale, ignored
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1, window: 0));
        lock (sync) sent.Clear();

        // the next write goes out at once
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await client.Stream.WriteAsync(new byte[500], cts.Token);
        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Payload.Length == 500), "the stale zero window must not hold the data");
        await client.DisposeAsync();
    }

    /// <summary>An ack of data never sent draws a challenge ACK and changes nothing (RFC 5961 5.2): its zero window
    /// is not applied, its data and FIN are dropped.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task AckOfUnsentData_DrawsAChallengeAck_AndAppliesNoWindow()
    {
        using var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, fin: true, seq: 1001, ackNum: serverSeq + 1 + 5000, window: 0, payload: new byte[100]));
        lock (sync) {
            Assert.HasCount(1, sent, "one challenge ACK");
            var challenge = sent[0].ExtractTcp();
            Assert.IsTrue(challenge is { Acknowledgment: true, Synchronize: false, Finish: false, Reset: false, Payload.Length: 0 }, "a pure ACK");
            Assert.AreEqual(serverSeq + 1, challenge.SequenceNumber);
            Assert.AreEqual(1001u, challenge.AcknowledgmentNumber);
            sent.Clear();
        }
        var readTask = client.Stream.ReadAsync(new byte[100]).AsTask();
        await Task.Delay(100);
        Assert.IsFalse(readTask.IsCompleted, "its data and FIN were dropped");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await client.Stream.WriteAsync(new byte[500], cts.Token);
        lock (sync)
            Assert.IsTrue(sent.Any(p => p.ExtractTcp().Payload.Length == 500), "the bogus zero window must not hold the data");
        await client.DisposeAsync();
    }

    /// <summary>After the handshake every segment carries an ACK: data without one is dropped unanswered (RFC 9293
    /// 3.10.7.4), and delivered once it comes with the ACK.</summary>
    [TestMethod]
    [Timeout(10000)]
    public async Task DataWithoutTheAckBit_IsDropped()
    {
        using var tcpStack = new LocalTcpStack();
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var (client, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        var payload = new byte[100];
        RandomNumberGenerator.Fill(payload);
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            psh: true, seq: 1001, payload: payload));
        var buffer = new byte[100];
        var readTask = client.Stream.ReadAsync(buffer).AsTask();
        await Task.Delay(100);
        lock (sync) Assert.IsEmpty(sent, "a segment without the ACK bit is dropped unanswered");
        Assert.IsFalse(readTask.IsCompleted, "nothing reached the app");

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: payload));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.AreEqual(100, await readTask.WaitAsync(cts.Token));
        CollectionAssert.AreEqual(payload, buffer);
        await client.DisposeAsync();
    }

    // ---- helpers ----

    /// <summary>Completes a handshake and returns the accepted client plus the server's ISN.</summary>
    private static async Task<(ITcpClient client, uint serverSeq)> EstablishAsync(
        LocalTcpStack tcpStack, List<IpPacket> sent, object sync)
    {
        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));
        var acceptTask = AcceptConnectionAsync(listener);

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000));
        uint serverSeq;
        lock (sync) {
            var synAck = sent.Last(p => p.ExtractTcp() is { Synchronize: true, Acknowledgment: true });
            serverSeq = synAck.ExtractTcp().SequenceNumber;
        }
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, seq: 1001, ackNum: serverSeq + 1));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var client = await acceptTask.WaitAsync(cts.Token);
        return (client, serverSeq);
    }

    private static async Task<ITcpClient> AcceptConnectionAsync(ITcpListener listener)
    {
        await foreach (var client in listener.AcceptAllAsync(CancellationToken.None))
            return client;
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

    private static async Task WaitForCondition(Func<bool> condition, CancellationToken ct, int timeoutMs = 1000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(5, ct);
    }
}
