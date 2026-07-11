using System.Net;
using System.Security.Cryptography;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.TcpStack.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.TcpStack.Test;

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
    public TestContext TestContext { get; set; } = null!;

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
    /// Ring/sequence integrity: when the retx ring is FULL, a Zero Window Probe must NOT consume a new
    /// byte it cannot store (the pre-fix behavior — a dropped probe then left a sequence number with no
    /// backing byte: silent stream corruption or a permanent stall). It must probe with the byte at
    /// SndUna instead, and the stream must survive intact once the peer resumes acknowledging.
    /// </summary>
    [TestMethod]
    [Timeout(15000)]
    public async Task ZeroWindowProbe_RingFull_NeverConsumesUnstorableBytes()
    {
        const int retxSize = 600;
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            MaxMss = 536,
            DefaultMss = 536,
            RetxBufferSize = retxSize,
            ZeroWindowProbeInterval = TimeSpan.FromMilliseconds(50),
            RetransmitTimeout = TimeSpan.FromSeconds(30), // keep the RTO out of this test
            RetransmitMaxTimeout = TimeSpan.FromSeconds(30)
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };

        var (stream, serverSeq) = await EstablishAsync(tcpStack, sent, sync);
        lock (sync) sent.Clear();

        // Server writes more than the ring can hold; the peer acks NOTHING, so the sender fills the
        // ring (peer window 65535 >> ring 600) and then stalls with allowable == 0 and a FULL ring —
        // exactly the state where the pre-fix probe consumed unstorable bytes.
        var payload = new byte[2000];
        RandomNumberGenerator.Fill(payload);
        var writeTask = stream.Stream.WriteAsync(payload, 0, payload.Length);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Wait until probes fire (packets beyond the initial ring-filling burst).
        await WaitForCondition(() => {
            lock (sync) return sent.Count(p => p.ExtractTcp().Payload.Length > 0) >= 4;
        }, cts.Token, 3000);
        await Task.Delay(200, cts.Token); // let several more probe intervals elapse

        var frontierLimit = serverSeq + 1 + retxSize; // first unsendable seq while nothing is acked
        lock (sync) {
            foreach (var p in sent.Where(p => p.ExtractTcp().Payload.Length > 0)) {
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
    /// drop at peak rate) while the writer is parked on a FULL retx ring with more data pending, then —
    /// like a real kernel — acks whatever arrives in order. This is the pathological regime for a
    /// 1-byte ring-full probe: each 1-byte hole fill elicits a +1 cumulative ACK that RESTARTS the RTO
    /// and resets the dup-ACK count, so neither the RTO nor fast retransmit ever fires and recovery
    /// crawls at ~2 bytes per probe interval (the 2026-07-01 speedtest "collapse to ~1 then slowly
    /// recover" incident). The ring-full probe must retransmit at SEGMENT granularity so the write
    /// completes promptly.
    /// </summary>
    [TestMethod]
    [Timeout(30000)]
    public async Task BurstLoss_RingFullProbe_RecoversAtSegmentGranularity()
    {
        const int retxSize = 2048;
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            MaxMss = 536,
            DefaultMss = 536,
            RetxBufferSize = retxSize,
            // Same ordering as production (200 ms ZWP < 500 ms RTO): the probe path, not the RTO,
            // must carry the recovery, because each probe-elicited ACK restarts the RTO.
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

        // With segment-granularity recovery this completes in well under a second; with a 1-byte
        // ring-full probe it needs ~1 byte per probe interval (minutes) and times out.
        await writeTask.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);
        await echo;

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
            RetxBufferSize = 1024,
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
        await foreach (var client in listener.AcceptAllAsync())
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
