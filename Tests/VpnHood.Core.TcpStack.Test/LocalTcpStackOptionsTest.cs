using System.Net;
using System.Security.Cryptography;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.TcpStack.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.TcpStack.Test;

/// <summary>
/// Tests for the configurable-memory surface (<see cref="LocalTcpStackOptions"/>): that defaults
/// reproduce the historical wire values (Android parity), that custom values are honored, that
/// validation rejects bad combinations, and that the new aggregate bounds work.
/// </summary>
[TestClass]
public sealed class LocalTcpStackOptionsTest
{
    private static readonly IPAddress ServerIp = IPAddress.Parse("10.0.0.1");
    private static readonly IPAddress ClientIp = IPAddress.Parse("10.0.0.2");
    private const int ServerPort = 8080;
    private const int ClientPort = 54321;

    /// <summary>
    /// Default options must advertise exactly the historical values (65535 window, 1460 MSS) so
    /// Android throughput, tuned against those constants, cannot regress.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public void DefaultOptions_SynAck_AdvertisesHistoricalWindowAndMss()
    {
        var tcpStack = new LocalTcpStack(LocalTcpStackOptions.Default); // explicit, host-independent
        var sent = new List<IpPacket>();
        tcpStack.OnPacketSend = sent.Add;
        tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000));

        var synAck = sent[0].ExtractTcp();
        Assert.AreEqual((ushort)0xFFFF, synAck.WindowSize, "default advertised window must stay 65535");
        Assert.AreEqual(1460, GetMssOption(synAck), "default advertised MSS must stay 1460");
    }

    /// <summary>
    /// Custom options must flow through to the advertised window and MSS in the SYN-ACK.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public void CustomOptions_SynAck_AdvertisesConfiguredWindowAndMss()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions { ReceiveWindowSize = 16 * 1024, MaxMss = 1000 });
        var sent = new List<IpPacket>();
        tcpStack.OnPacketSend = sent.Add;
        tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));

        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000));

        var synAck = sent[0].ExtractTcp();
        Assert.AreEqual((ushort)(16 * 1024), synAck.WindowSize, "advertised window must equal ReceiveWindowSize");
        Assert.AreEqual(1000, GetMssOption(synAck), "advertised MSS must equal MaxMss");
    }

    /// <summary>
    /// A small receive window must still round-trip client→server data correctly (Upload path /
    /// reassembly pipe sized from ReceiveWindowSize).
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task SmallReceiveWindow_ClientToServerData_RoundTrips()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions { ReceiveWindowSize = 16 * 1024, RetxBufferSize = 8 * 1024 });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var serverSeq = SendSyn(tcpStack, sent, sync, ClientPort, 1000);
        SendFinalAck(tcpStack, ClientPort, 1000, serverSeq);
        var stream = await AcceptConnectionAsync(listener).WaitAsync(cts.Token);

        var data = new byte[4096];
        RandomNumberGenerator.Fill(data);
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
            ack: true, psh: true, seq: 1001, ackNum: serverSeq + 1, payload: data));

        var received = new List<byte>();
        var buffer = new byte[1024];
        while (received.Count < data.Length) {
            var n = await stream.Stream.ReadAsync(buffer, cts.Token);
            if (n == 0) break;
            received.AddRange(buffer.Take(n));
        }

        CollectionAssert.AreEqual(data, received.ToArray(), "all data must round-trip through a small window");
        await stream.DisposeAsync();
    }

    /// <summary>
    /// A small retransmission ring (server/Download side) must round-trip server→client data,
    /// exercising the ring-buffer wrap with the configured (non-default) capacity. The test acts
    /// as the client: it collects server segments, cumulative-ACKs them to free the ring, and
    /// reassembles in sequence order.
    /// </summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task SmallRetxBuffer_ServerToClientData_RoundTripsWithRingWrap()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions {
            ReceiveWindowSize = 8 * 1024,
            RetxBufferSize = 2 * 1024,   // small ring -> forces multiple wraps
            MaxMss = 512,
            DefaultMss = 512,            // peer SYN advertises none -> server uses this
        });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverSeq = SendSyn(tcpStack, sent, sync, ClientPort, 1000);
        SendFinalAck(tcpStack, ClientPort, 1000, serverSeq);
        var stream = await AcceptConnectionAsync(listener).WaitAsync(cts.Token);
        lock (sync) sent.Clear();

        var payload = new byte[8 * 1024]; // 4x the ring capacity
        RandomNumberGenerator.Fill(payload);
        var writeTask = stream.Stream.WriteAsync(payload, cts.Token).AsTask();

        // Drive the send loop: read server data segments, cumulative-ACK, reassemble in order.
        var reassembled = new List<byte>();
        var expectedNext = serverSeq + 1; // server data starts at ISN+1
        var consumed = 0;
        while (reassembled.Count < payload.Length) {
            cts.Token.ThrowIfCancellationRequested();
            IpPacket[] batch;
            lock (sync) { batch = sent.Skip(consumed).ToArray(); consumed = sent.Count; }

            var progressed = false;
            foreach (var p in batch) {
                var tcp = p.ExtractTcp();
                if (tcp.Payload.Length == 0) continue;
                if (tcp.SequenceNumber != expectedNext) continue; // ignore any out-of-order/retx
                reassembled.AddRange(tcp.Payload.ToArray());
                expectedNext += (uint)tcp.Payload.Length;
                progressed = true;
            }

            if (progressed)
                tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort,
                    ack: true, seq: 1001, ackNum: expectedNext));
            else
                await Task.Delay(5, cts.Token);
        }

        await writeTask.WaitAsync(cts.Token);
        CollectionAssert.AreEqual(payload, reassembled.ToArray(), "all data must round-trip through a small retx ring");
        await stream.DisposeAsync();
    }

    /// <summary>
    /// Validation must reject memory-option combinations that would be unsafe or self-inconsistent.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public void Validation_RejectsInvalidOptionCombinations()
    {
        Assert.Throws<ArgumentException>(() => new LocalTcpStack(new LocalTcpStackOptions { ReceiveWindowSize = 70_000 }),
            "window > 65535 (no scaling) must be rejected");
        Assert.Throws<ArgumentException>(() => new LocalTcpStack(new LocalTcpStackOptions { ReceiveWindowSize = 0 }),
            "zero window must be rejected");
        Assert.Throws<ArgumentException>(() => new LocalTcpStack(new LocalTcpStackOptions { ReceiveWindowSize = 16384, PipeResumeWriterThreshold = 16384 }),
            "resume threshold >= window must be rejected");
        Assert.Throws<ArgumentException>(() => new LocalTcpStack(new LocalTcpStackOptions { GlobalReceiveBudget = 0 }),
            "non-positive global receive budget must be rejected");
        Assert.Throws<ArgumentException>(() => new LocalTcpStack(new LocalTcpStackOptions { RetxBufferSize = 256, MaxMss = 1460 }),
            "retx buffer smaller than MaxMss must be rejected");
        Assert.Throws<ArgumentException>(() => new LocalTcpStack(new LocalTcpStackOptions { IdleTimeout = TimeSpan.FromSeconds(30), IdleCheckInterval = TimeSpan.FromMinutes(1) }),
            "idle timeout <= idle check interval must be rejected");
        Assert.Throws<ArgumentException>(() => new LocalTcpStack(new LocalTcpStackOptions { MaxConnections = -1 }),
            "negative MaxConnections must be rejected");
    }

    /// <summary>
    /// Default options validate, and both presets are constructible (sanity check on the presets).
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public void Presets_AreValid()
    {
        using var def = new LocalTcpStack(LocalTcpStackOptions.Default);
        using var ios = new LocalTcpStack(LocalTcpStackOptions.Ios);
        Assert.AreEqual(0xFFFF, LocalTcpStackOptions.Ios.ReceiveWindowSize);
        Assert.AreEqual(40, LocalTcpStackOptions.Ios.MaxConnections);
    }

    /// <summary>
    /// The auto-detected default (what <c>new LocalTcpStack()</c> uses) must be full-size on any
    /// non-Apple-mobile host (the unit-test host is Windows/Linux/desktop-macOS), guaranteeing no
    /// footprint/throughput change for Android and desktop.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public void ForCurrentPlatform_OnNonAppleMobileHost_ReturnsFullSizeDefault()
    {
        var opt = LocalTcpStackOptions.ForCurrentPlatform();
        Assert.AreEqual(LocalTcpStackOptions.Default.ReceiveWindowSize, opt.ReceiveWindowSize);
        Assert.AreEqual(LocalTcpStackOptions.Default.RetxBufferSize, opt.RetxBufferSize);
        Assert.AreEqual(0, opt.MaxConnections, "non-Apple-mobile hosts must not impose a connection cap");
    }

    /// <summary>
    /// With MaxConnections set, a SYN beyond the cap is answered with an RST instead of a SYN-ACK.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public void MaxConnections_OverLimitSyn_GetsRst()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions { MaxConnections = 1 });
        var sent = new List<IpPacket>();
        tcpStack.OnPacketSend = sent.Add;
        tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));

        // First SYN -> creates connection #1 -> SYN-ACK.
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort, ServerIp, ServerPort, syn: true, seq: 1000));
        Assert.IsTrue(sent[^1].ExtractTcp() is { Synchronize: true, Acknowledgment: true }, "first SYN must get a SYN-ACK");

        // Second SYN (different port) -> over cap -> RST, no new SYN-ACK.
        sent.Clear();
        tcpStack.ProcessIncoming(CreateTcpPacket(ClientIp, ClientPort + 1, ServerIp, ServerPort, syn: true, seq: 2000));
        Assert.AreEqual(1, sent.Count, "over-cap SYN must produce exactly one reply");
        Assert.IsTrue(sent[0].ExtractTcp().Reset, "over-cap SYN must get a RST");
    }

    /// <summary>
    /// A bounded accepts queue must drop the overflow gracefully (no exception) and never hand the
    /// application more than the configured backlog.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task BoundedAcceptQueue_WhenFull_DropsOverflowGracefully()
    {
        var tcpStack = new LocalTcpStack(new LocalTcpStackOptions { AcceptQueueCapacity = 1 });
        var sent = new List<IpPacket>();
        object sync = new();
        tcpStack.OnPacketSend = p => { lock (sync) sent.Add(p); };
        var listener = tcpStack.Listen(new IpEndPointValue(ServerIp, ServerPort));

        // Establish two connections without accepting any -> the 2nd overflows the 1-slot queue.
        for (var i = 0; i < 2; i++) {
            var clientPort = ClientPort + i;
            var clientIsn = (uint)(1000 + i * 1000);
            var serverSeq = SendSyn(tcpStack, sent, sync, clientPort, clientIsn);
            SendFinalAck(tcpStack, clientPort, clientIsn, serverSeq); // MarkEstablished -> enqueue (2nd dropped)
        }

        // Exactly one client is accepted; the overflow was dropped (no throw above).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var first = await listener.AcceptAsync(cts.Token);
        Assert.IsNotNull(first);

        // No second client is queued -> the next accept blocks until cancelled.
        var threw = false;
        using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try { await listener.AcceptAsync(cts2.Token); }
        catch (OperationCanceledException) { threw = true; }
        Assert.IsTrue(threw, "no second client should be queued");
    }

    // ---- helpers ----

    private static uint SendSyn(LocalTcpStack stack, List<IpPacket> sent, object sync, int clientPort, uint clientIsn)
    {
        int before;
        lock (sync) before = sent.Count;
        stack.ProcessIncoming(CreateTcpPacket(ClientIp, clientPort, ServerIp, ServerPort, syn: true, seq: clientIsn));
        lock (sync) return sent[before].ExtractTcp().SequenceNumber; // ISN of SYN-ACK
    }

    private static void SendFinalAck(LocalTcpStack stack, int clientPort, uint clientIsn, uint serverSeq)
    {
        stack.ProcessIncoming(CreateTcpPacket(ClientIp, clientPort, ServerIp, ServerPort,
            ack: true, seq: clientIsn + 1, ackNum: serverSeq + 1));
    }

    private static async Task<ITcpClient> AcceptConnectionAsync(ITcpListener listener)
    {
        await foreach (var client in listener.AcceptAllAsync())
            return client;
        throw new InvalidOperationException("No connection accepted");
    }

    /// <summary>Parses the MSS (kind=2,len=4) option value from a packet's TCP options, or -1 if absent.</summary>
    private static int GetMssOption(TcpPacket tcp)
    {
        var opts = tcp.Options.Span;
        var i = 0;
        while (i < opts.Length) {
            var kind = opts[i];
            if (kind == 0) break;       // end of options
            if (kind == 1) { i++; continue; } // NOP
            if (i + 1 >= opts.Length) break;
            var len = opts[i + 1];
            if (len < 2 || i + len > opts.Length) break;
            if (kind == 2 && len == 4) return (opts[i + 2] << 8) | opts[i + 3];
            i += len;
        }
        return -1;
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
}
