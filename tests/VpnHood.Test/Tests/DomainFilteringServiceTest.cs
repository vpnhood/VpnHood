using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Filtering.DomainFiltering.Observation;
using VpnHood.Net.Packets;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class DomainFilteringServiceTest : TestBase
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Parse("198.51.100.1"), 443);

    // gives its pieces one at a read, as a connection gives what has arrived so far, a piece larger than the read in
    // parts
    private sealed class PiecewiseStream(params byte[][] pieces) : Stream
    {
        private int _pieceIndex;
        private int _pieceOffset;
        public int ReadCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            if (_pieceIndex >= pieces.Length)
                return 0;

            var piece = pieces[_pieceIndex];
            var length = Math.Min(count, piece.Length - _pieceOffset);
            Array.Copy(piece, _pieceOffset, buffer, offset, length);
            _pieceOffset += length;
            if (_pieceOffset == piece.Length) {
                _pieceIndex++;
                _pieceOffset = 0;
            }

            return length;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // gives its pieces one at a read, each after a delay, then waits for more that never come, as a connection that
    // goes quiet
    private sealed class StallingStream(params byte[][] pieces) : Stream
    {
        private int _pieceIndex;
        public TimeSpan PieceDelay { get; init; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_pieceIndex < pieces.Length) {
                if (PieceDelay > TimeSpan.Zero)
                    await Task.Delay(PieceDelay, cancellationToken);

                var piece = pieces[_pieceIndex++];
                piece.CopyTo(buffer);
                return piece.Length;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static DomainFilteringService CreateService(params string[] blockedDomains)
    {
        return new DomainFilteringService(new StaticDomainFilter(nextFilter: null) { Blocks = blockedDomains },
            new EventId(), tlsBufferSize: 4096) {
            IsEnabled = true
        };
    }

    // a TLS record that holds a ClientHello with a server name, after a padding extension of the given length
    internal static byte[] BuildClientHello(string serverName, int paddingLength = 0)
    {
        var name = Encoding.ASCII.GetBytes(serverName);
        byte[] serverNameExtension = [
            0x00, 0x00, .. Length(name.Length + 5), .. Length(name.Length + 3), 0x00, .. Length(name.Length), .. name
        ];
        byte[] paddingExtension = paddingLength > 0 ? [0x00, 0x15, .. Length(paddingLength), .. new byte[paddingLength]] : [];
        byte[] extensions = [.. paddingExtension, .. serverNameExtension];
        byte[] body = [
            0x03, 0x03, .. new byte[32], 0x00, 0x00, 0x02, 0x13, 0x01, 0x01, 0x00,
            .. Length(extensions.Length), .. extensions
        ];
        byte[] handshake = [0x01, 0x00, .. Length(body.Length), .. body];
        return [0x16, 0x03, 0x01, .. Length(handshake.Length), .. handshake];
    }

    // the same ClientHello in two TLS records, the handshake cut where given
    private static byte[] SplitIntoTwoRecords(byte[] clientHello, int cut)
    {
        var handshake = clientHello[5..];
        return [
            0x16, 0x03, 0x01, .. Length(cut), .. handshake[..cut],
            0x16, 0x03, 0x01, .. Length(handshake.Length - cut), .. handshake[cut..]
        ];
    }

    private static byte[] Length(int value) => [(byte)(value >> 8), (byte)value];

    [TestMethod]
    public void The_close_closes_the_QUIC_SNI_service()
    {
        var service = new DomainFilteringService(new StaticDomainFilter(nextFilter: null), new EventId(),
            tlsBufferSize: 1000) {
            IsEnabled = true
        };

        service.Dispose();
        using var ipPacket = PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"),
            sourcePort: 50000, destinationPort: 443, payload: new byte[16]);
        Assert.ThrowsExactly<ObjectDisposedException>(() => service.ProcessPacket(ipPacket));
    }

    [TestMethod]
    public async Task A_ClientHello_that_comes_in_pieces_gives_its_name()
    {
        using var service = CreateService("blocked.example");
        var clientHello = BuildClientHello("www.blocked.example");

        // three pieces, as a large ClientHello comes in several segments: the first ends inside the record's header,
        // the second before the name
        await using var stream = new PiecewiseStream(clientHello[..3], clientHello[3..40], clientHello[40..]);
        var result = await service.ProcessStream(stream, RemoteEndPoint, TestCt);

        // one read gave the first piece alone, with no name in it, and the domain rules missed the connection
        Assert.AreEqual("www.blocked.example", result.DomainName);
        Assert.AreEqual(FilterAction.Block, result.Action);
        CollectionAssert.AreEqual(clientHello, result.ReadData.ToArray());
    }

    [TestMethod]
    public async Task Bytes_that_begin_no_ClientHello_are_read_once()
    {
        using var service = CreateService("blocked.example");
        var request = "GET / HTTP/1.1\r\n"u8.ToArray();

        // not TLS: nothing says more is to come, and the client may wait for an answer to what it has sent
        await using var stream = new PiecewiseStream(request, "Host: blocked.example\r\n\r\n"u8.ToArray());
        var result = await service.ProcessStream(stream, RemoteEndPoint, TestCt);
        Assert.IsNull(result.DomainName);
        CollectionAssert.AreEqual(request, result.ReadData.ToArray());
        Assert.AreEqual(1, stream.ReadCount);
    }

    [TestMethod]
    public async Task A_stream_that_sends_nothing_goes_on_with_no_name()
    {
        using var service = CreateService("blocked.example");

        // as a mail, FTP or database client, which waits for its server to speak first: the server is reached only
        // after the verdict, and the wait for the client's bytes never ended. The test's own limit cancels the call,
        // which is not the service's answer
        await using var stream = new StallingStream();
        using var testLimitCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        testLimitCts.CancelAfter(TimeSpan.FromSeconds(10));
        var result = await service.ProcessStream(stream, RemoteEndPoint, testLimitCts.Token);
        Assert.IsNull(result.DomainName);
        Assert.AreEqual(0, result.ReadData.Length);
        Assert.AreEqual(FilterAction.Default, result.Action);
    }

    [TestMethod]
    public async Task A_ClientHello_left_unfinished_fails_rather_than_pass_the_domain_rules()
    {
        using var service = CreateService("blocked.example");
        var clientHello = BuildClientHello("www.blocked.example");

        // its name is in the part that never comes: gone on with no name, the connection would pass the block
        await using var stream = new StallingStream(clientHello[..40]);
        using var testLimitCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        testLimitCts.CancelAfter(TimeSpan.FromSeconds(10));
        await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            service.ProcessStream(stream, RemoteEndPoint, testLimitCts.Token));
    }

    [TestMethod]
    [DataRow(20)]
    [DataRow(2)]
    public async Task A_ClientHello_in_two_records_gives_its_name(int cut)
    {
        using var service = CreateService("blocked.example");

        // The second record holds the name: the first was read alone, and the connection passed with no name. Cut at
        // 2, the first record ends inside the handshake's own header: its length was read from the bytes that
        // follow, the next record's header, and the ClientHello never seemed complete
        var clientHello = SplitIntoTwoRecords(BuildClientHello("www.blocked.example"), cut);
        await using var stream = new PiecewiseStream(clientHello);
        var result = await service.ProcessStream(stream, RemoteEndPoint, TestCt);
        Assert.AreEqual("www.blocked.example", result.DomainName);
        Assert.AreEqual(FilterAction.Block, result.Action);
        CollectionAssert.AreEqual(clientHello, result.ReadData.ToArray());
    }

    [TestMethod]
    public async Task A_ClientHello_that_the_stream_ends_early_fails()
    {
        using var service = CreateService("blocked.example");

        // the name is in the part that never comes: read as all there is, the connection passed with no name
        await using var stream = new PiecewiseStream(BuildClientHello("www.blocked.example")[..40]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            service.ProcessStream(stream, RemoteEndPoint, TestCt));
    }

    [TestMethod]
    public async Task A_ClientHello_cut_by_a_record_of_another_type_fails()
    {
        using var service = CreateService("blocked.example");

        // The name is in a handshake record that would come after the other record, which TLS allows none of inside a
        // ClientHello: the read ended there, and the connection passed with no name
        var handshake = BuildClientHello("www.blocked.example")[5..];
        byte[] bytes = [
            0x16, 0x03, 0x01, .. Length(20), .. handshake[..20],
            0x17, 0x03, 0x03, .. Length(5), 1, 2, 3, 4, 5
        ];
        await using var stream = new PiecewiseStream(bytes);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            service.ProcessStream(stream, RemoteEndPoint, TestCt));
    }

    [TestMethod]
    public async Task A_ClientHello_larger_than_the_buffer_fails()
    {
        // the 4 KB service's read stops short of the name, past the padding
        using var service = CreateService("blocked.example");
        var clientHello = BuildClientHello("www.blocked.example", paddingLength: 5000);
        await using var stream = new PiecewiseStream(clientHello);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            service.ProcessStream(stream, RemoteEndPoint, TestCt));
    }

    [TestMethod]
    public async Task A_ClientHello_of_a_whole_record_gives_its_name_in_the_client_buffer()
    {
        // the client's buffer, a record and its header, holds a name past 4 KB, which a read of 4 KB missed
        using var service = new DomainFilteringService(
            new StaticDomainFilter(nextFilter: null) { Blocks = ["blocked.example"] }, new EventId(),
            tlsBufferSize: 5 + 16 * 1024) { IsEnabled = true };
        var clientHello = BuildClientHello("www.blocked.example", paddingLength: 5000);
        await using var stream = new PiecewiseStream(clientHello);
        var result = await service.ProcessStream(stream, RemoteEndPoint, TestCt);
        Assert.AreEqual("www.blocked.example", result.DomainName);
    }

    [TestMethod]
    public async Task A_read_canceled_by_its_caller_is_canceled_not_given_up()
    {
        using var service = CreateService("blocked.example");

        // the caller's cancellation, before the second is out, is neither a stream with no name nor a ClientHello late
        await using var stream = new StallingStream();
        using var callerCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        callerCts.CancelAfter(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ProcessStream(stream, RemoteEndPoint, callerCts.Token));
    }

    [TestMethod]
    public async Task A_ClientHello_whose_pieces_come_slowly_fails_once_its_second_is_out()
    {
        using var service = CreateService("blocked.example");
        var clientHello = BuildClientHello("www.blocked.example");

        // each piece comes within the second, and all of them do not: a limit that starts again at each read lets a
        // name take any time
        await using var stream = new StallingStream(clientHello[..20], clientHello[20..40], clientHello[40..60],
            clientHello[60..]) { PieceDelay = TimeSpan.FromMilliseconds(400) };
        using var testLimitCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        testLimitCts.CancelAfter(TimeSpan.FromSeconds(10));
        await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            service.ProcessStream(stream, RemoteEndPoint, testLimitCts.Token));
    }

    [TestMethod]
    public void A_DNS_query_is_not_held_for_a_QUIC_name()
    {
        using var service = CreateService("blocked.example");

        // A standard query whose ID begins with a set bit, as half of them do: read as a QUIC long header, its
        // flags and counts make a version, connection IDs and lengths that fit, and the query was held for a name
        // it never holds, until the flow's next packet or its expiry
        byte[] query = [
            0x80, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x07, .. "example"u8, 0x03, .. "com"u8, 0x00, 0x00, 0x01, 0x00, 0x01
        ];
        using var packet = PacketBuilder.BuildUdp(new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50000),
            new IPEndPoint(IPAddress.Parse("198.51.100.53"), 53), query);
        var result = service.ProcessPacket(packet);
        Assert.IsFalse(result.NeedMore);
        Assert.AreEqual(FilterAction.Default, result.Action);
    }

    [TestMethod]
    public void A_datagram_with_a_length_past_its_end_is_no_QUIC()
    {
        using var service = CreateService("blocked.example");

        // The header of a client Initial of version 1, whose token length, a number of eight bytes, is 2 GB: narrowed
        // to an int it went negative, the next read began before the datagram's start, and the reader threw
        var datagram = new byte[1200];
        datagram[0] = 0xC0; // a long header of an Initial
        datagram[4] = 0x01; // version 1
        datagram[5] = 8; // a destination connection ID of eight bytes, then a source one of none
        datagram[15] = 0xC0; // the token's length: eight bytes that hold 0x80000000
        datagram[19] = 0x80;
        using var packet = PacketBuilder.BuildUdp(new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50007),
            new IPEndPoint(IPAddress.Parse("198.51.100.9"), 443), datagram);
        var result = service.ProcessPacket(packet);
        Assert.IsFalse(result.NeedMore);
        Assert.AreEqual(FilterAction.Default, result.Action);
    }

    [TestMethod]
    public void Flows_counted_at_the_same_moment_are_all_counted()
    {
        const int threadCount = 8;
        const int countPerThread = 200_000;
        var stat = new DomainObserverStat();
        Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, _ => {
            for (var i = 0; i < countPerThread; i++)
                stat.Update(FilterAction.Block);
        });

        Assert.AreEqual(threadCount * countPerThread, stat.TotalCount);
        Assert.AreEqual(threadCount * countPerThread, stat.BlockedCount);
    }

    // The first datagrams of a QUIC connection to a name, as a real client sends them: its Initial packets, which
    // hold the name. Nothing answers, so the connection is given up once they are in. A large hello, made so by long
    // application protocol names, takes two datagrams, as a browser's with a post-quantum key share does, and more
    // with more of those names.
    private async Task<IReadOnlyList<byte[]>> CaptureQuicInitial(string serverName, bool largeHello = false,
        int largeProtocolCount = 8)
    {
        List<SslApplicationProtocol> applicationProtocols = [SslApplicationProtocol.Http3];
        if (largeHello)
            applicationProtocols.AddRange(Enumerable.Range(0, largeProtocolCount)
                .Select(i => new SslApplicationProtocol(new string((char)('a' + i), 200))));

        using var udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var localEndPoint = (IPEndPoint?)udpClient.Client.LocalEndPoint ??
                            throw new InvalidOperationException("The socket has no local end point.");

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var connectTask = QuicConnection.ConnectAsync(new QuicClientConnectionOptions {
            RemoteEndPoint = localEndPoint,
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions {
                TargetHost = serverName,
                ApplicationProtocols = applicationProtocols
            }
        }, connectCts.Token).AsTask();

        // a ClientHello with a large key share takes two datagrams or more, sent together, well before any is sent again
        var datagrams = new List<byte[]>();
        var first = await udpClient.ReceiveAsync(TestCt).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        datagrams.Add(first.Buffer);
        try {
            while (true) {
                var next = await udpClient.ReceiveAsync(TestCt).AsTask().WaitAsync(TimeSpan.FromMilliseconds(200), TestCt);
                datagrams.Add(next.Buffer);
            }
        }
        catch (TimeoutException) {
            // the datagrams so far held it all
        }

        await connectCts.CancelAsync();
        await Assert.ThrowsAsync<Exception>(() => connectTask);
        return datagrams;
    }

    [TestMethod]
    public async Task A_QUIC_flow_to_another_destination_has_a_verdict_of_its_own()
    {
        if (!QuicConnection.IsSupported)
            Assert.Inconclusive("This machine has no QUIC to make a real Initial packet with.");

        using var service = CreateService("blocked.example");
        var datagrams = await CaptureQuicInitial("www.blocked.example");
        var source = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50000);

        // the port's first flow is not QUIC, and the service gives it up: no objection
        using var plainPacket = PacketBuilder.BuildUdp(source, new IPEndPoint(IPAddress.Parse("198.51.100.1"), 443),
            new byte[32]);
        Assert.AreEqual(FilterAction.Default, service.ProcessPacket(plainPacket).Action);

        // The same port then opens a QUIC flow to another server, for a blocked name. Known by its source alone, it
        // took the first flow's verdict, and its name was never read
        var result = PacketSniFilterResult.Passthrough();
        var packets = new List<IpPacket>();
        foreach (var datagram in datagrams) {
            var packet = PacketBuilder.BuildUdp(source, new IPEndPoint(IPAddress.Parse("198.51.100.2"), 443), datagram);
            packets.Add(packet);
            result = service.ProcessPacket(packet);
            if (!result.NeedMore)
                break;
        }

        Assert.AreEqual("www.blocked.example", result.DomainName);
        Assert.AreEqual(FilterAction.Block, result.Action);
        foreach (var packet in packets)
            packet.Dispose();
    }

    [TestMethod]
    public async Task A_QUIC_connection_after_other_UDP_on_the_same_flow_has_its_name_read()
    {
        if (!QuicConnection.IsSupported)
            Assert.Inconclusive("This machine has no QUIC to make a real Initial packet with.");

        using var service = CreateService("blocked.example");
        var datagrams = await CaptureQuicInitial("www.blocked.example");
        var source = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50004);
        var destination = new IPEndPoint(IPAddress.Parse("198.51.100.6"), 443);
        var packets = new List<IpPacket>();
        try {
            // the port's first datagram is no QUIC, and the flow has no objection
            Assert.AreEqual(FilterAction.Default, FeedQuic(service, source, destination, [new byte[32]], packets).Action);

            // A QUIC connection then opens on the same addresses and ports, for a blocked name: it took the flow's
            // verdict, and its name was never read
            var result = FeedQuic(service, source, destination, datagrams, packets);
            Assert.AreEqual("www.blocked.example", result.DomainName);
            Assert.AreEqual(FilterAction.Block, result.Action);
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    public async Task A_QUIC_ClientHello_left_unfinished_drops_its_flow()
    {
        if (!QuicConnection.IsSupported)
            Assert.Inconclusive("This machine has no QUIC to make a real Initial packet with.");

        using var service = CreateService("blocked.example");
        var datagrams = await CaptureQuicInitial("www.blocked.example", largeHello: true);
        Assert.AreEqual(2, datagrams.Count, "The large hello must take two datagrams.");
        var source = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50002);
        var destination = new IPEndPoint(IPAddress.Parse("198.51.100.4"), 443);
        var packets = new List<IpPacket>();
        try {
            // The part of the hello that holds the name never comes, as when it is lost, and the flow's next packet
            // comes once the time is out: the flow passed with no name, and with it a blocked name, or an included one
            // out of the tunnel
            Assert.IsTrue(FeedQuic(service, source, destination, [datagrams[0]], packets).NeedMore);
            await Task.Delay(TimeSpan.FromSeconds(1.2), TestCt);
            var result = FeedQuic(service, source, destination, [BuildQuicShortHeaderDatagram()], packets);
            Assert.IsFalse(result.NeedMore);
            Assert.AreEqual(FilterAction.Block, result.Action);
            Assert.AreSame(packets[0], result.Packets.Single(), "the held datagram goes with the verdict");
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    public async Task A_QUIC_ClientHello_of_many_datagrams_gives_its_name()
    {
        if (!QuicConnection.IsSupported)
            Assert.Inconclusive("This machine has no QUIC to make a real Initial packet with.");

        // a hello of over 4 KB, as one with a post-quantum key share and a large session ticket
        using var service = CreateService("blocked.example");
        var datagrams = await CaptureQuicInitial("www.allowed.example", largeHello: true, largeProtocolCount: 20);
        Assert.IsGreaterThan(3, datagrams.Count, "The hello must take more than three datagrams.");
        var packets = new List<IpPacket>();
        try {
            // the reader gave a ClientHello three datagrams, and dropped the flow of one that takes more
            var result = FeedQuic(service, new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50006),
                new IPEndPoint(IPAddress.Parse("198.51.100.8"), 443), datagrams, packets);
            Assert.AreEqual("www.allowed.example", result.DomainName);
            Assert.AreEqual(FilterAction.Default, result.Action);
            Assert.HasCount(datagrams.Count - 1, result.Packets, "the held datagrams go with the verdict");
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    public async Task A_whole_QUIC_ClientHello_with_no_name_is_not_held()
    {
        if (!QuicConnection.IsSupported)
            Assert.Inconclusive("This machine has no QUIC to make a real Initial packet with.");

        // a connection to an address, whose hello names no server
        using var service = CreateService("blocked.example");
        var datagrams = await CaptureQuicInitial("127.0.0.1");
        Assert.AreEqual(1, datagrams.Count, "The hello must fit in one datagram.");
        var packets = new List<IpPacket>();
        try {
            var result = FeedQuic(service, new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50003),
                new IPEndPoint(IPAddress.Parse("198.51.100.5"), 443), datagrams, packets);

            // all of it was in, and it was held for a name it does not hold
            Assert.IsFalse(result.NeedMore);
            Assert.IsNull(result.DomainName);
            Assert.AreEqual(FilterAction.Default, result.Action);
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    // feeds a connection's first datagrams on a flow, and gives the result of the last one it took
    private static PacketSniFilterResult FeedQuic(DomainFilteringService service, IPEndPoint source,
        IPEndPoint destination, IReadOnlyList<byte[]> datagrams, List<IpPacket> packets)
    {
        var result = PacketSniFilterResult.Passthrough();
        foreach (var datagram in datagrams) {
            var packet = PacketBuilder.BuildUdp(source, destination, datagram);
            packets.Add(packet);
            result = service.ProcessPacket(packet);
            if (!result.NeedMore)
                break;
        }

        return result;
    }

    [TestMethod]
    public async Task Another_QUIC_connection_on_the_same_flow_has_its_name_read()
    {
        if (!QuicConnection.IsSupported)
            Assert.Inconclusive("This machine has no QUIC to make a real Initial packet with.");

        using var service = CreateService("blocked.example");
        var allowedDatagrams = await CaptureQuicInitial("www.allowed.example", largeHello: true);
        Assert.AreEqual(2, allowedDatagrams.Count, "The large hello must take two datagrams for the resend below.");
        var blockedDatagrams = await CaptureQuicInitial("www.blocked.example");
        var source = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50001);
        var destination = new IPEndPoint(IPAddress.Parse("198.51.100.3"), 443);
        var packets = new List<IpPacket>();
        try {
            Assert.AreEqual(FilterAction.Default, FeedQuic(service, source, destination, allowedDatagrams, packets).Action);

            // An Initial that begins no connection, as one the client sends after the server's first answer, which
            // names the server's connection ID: the flow keeps its verdict. Made here by changing the ID's first byte
            var laterInitial = allowedDatagrams[0].ToArray();
            laterInitial[6] ^= 0xFF;
            var laterResult = FeedQuic(service, source, destination, [laterInitial], packets);
            Assert.IsFalse(laterResult.NeedMore);
            Assert.AreEqual("www.allowed.example", laterResult.DomainName);

            // The client's first Initial sent again, as after a loss, belongs to the same connection: read as a new
            // one, its part of a ClientHello in two datagrams would be held for the other part, which the server may
            // have already
            var resentResult = FeedQuic(service, source, destination, [allowedDatagrams[0]], packets);
            Assert.IsFalse(resentResult.NeedMore);
            Assert.AreEqual("www.allowed.example", resentResult.DomainName);

            // The client then opens another connection from the same socket, to a blocked name on the same address,
            // as a CDN's: the flow took the first connection's verdict, and the name was never read
            var result = FeedQuic(service, source, destination, blockedDatagrams, packets);
            Assert.AreEqual("www.blocked.example", result.DomainName);
            Assert.AreEqual(FilterAction.Block, result.Action);
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    public async Task A_QUIC_connection_begun_while_another_is_read_has_its_own_name_read()
    {
        if (!QuicConnection.IsSupported)
            Assert.Inconclusive("This machine has no QUIC to make a real Initial packet with.");

        using var service = CreateService("blocked.example");
        var allowedDatagrams = await CaptureQuicInitial("www.allowed.example", largeHello: true);
        var blockedDatagrams = await CaptureQuicInitial("www.blocked.example", largeHello: true);
        Assert.AreEqual(2, allowedDatagrams.Count, "The large hello must take two datagrams.");
        Assert.AreEqual(2, blockedDatagrams.Count, "The large hello must take two datagrams.");
        var source = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50005);
        var destination = new IPEndPoint(IPAddress.Parse("198.51.100.7"), 443);
        var packets = new List<IpPacket>();
        try {
            // A client opens two connections at once from one socket, to two names on the same address, and the
            // second begins when half the first's ClientHello is in. Held with the first's packets, the second's
            // Initials waited for the first's verdict and left with it, their own name never read
            Assert.IsTrue(FeedQuic(service, source, destination, [allowedDatagrams[0]], packets).NeedMore);
            Assert.IsTrue(FeedQuic(service, source, destination, [blockedDatagrams[0]], packets).NeedMore);
            var result = FeedQuic(service, source, destination, [blockedDatagrams[1]], packets);
            Assert.AreEqual("www.blocked.example", result.DomainName);
            Assert.AreEqual(FilterAction.Block, result.Action);

            // the first connection's Initial is not the second's to release: given up, its client sends it again
            Assert.AreSame(packets[1], result.Packets.Single());
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    // a packet of a QUIC connection past its handshake: a short header, which names no connection the service can read
    private static byte[] BuildQuicShortHeaderDatagram() => TestQuicClient.BuildShortHeaderDatagram();

    // a ClientHello's handshake message alone, as QUIC carries it: with no TLS record around it
    private static byte[] BuildHandshake(string serverName) => BuildClientHello(serverName)[5..];

    // a ClientHello that is whole by its own length and not well formed inside: its cipher suites run past its end
    private static byte[] BuildMalformedHandshake(string serverName)
    {
        var handshake = BuildHandshake(serverName);
        const int cipherSuitesLengthOffset = 4 + 2 + 32 + 1;
        handshake[cipherSuitesLengthOffset] = 0xFF;
        handshake[cipherSuitesLengthOffset + 1] = 0xFF;
        return handshake;
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(16)]
    public void A_QUIC_ClientHello_gives_its_name_from_as_many_datagrams_as_it_takes(int datagramCount)
    {
        using var service = CreateService("blocked.example");
        var datagrams = new TestQuicClient().BuildInitials(BuildHandshake("www.blocked.example"), datagramCount);
        var packets = new List<IpPacket>();
        try {
            var result = FeedQuic(service, new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50011),
                new IPEndPoint(IPAddress.Parse("198.51.100.13"), 443), datagrams, packets);
            Assert.AreEqual("www.blocked.example", result.DomainName);
            Assert.AreEqual(FilterAction.Block, result.Action);
            Assert.HasCount(datagramCount, packets, "every datagram is read");
            Assert.HasCount(datagramCount - 1, result.Packets, "the held datagrams go with the verdict");
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    public void A_QUIC_ClientHello_in_more_datagrams_than_the_reader_takes_is_dropped()
    {
        using var service = CreateService("blocked.example");
        var datagrams = new TestQuicClient().BuildInitials(BuildHandshake("www.allowed.example"), datagramCount: 17);
        var packets = new List<IpPacket>();
        try {
            // the reader takes sixteen, and the name is not whole without the last: dropped, not let through unnamed
            var result = FeedQuic(service, new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50012),
                new IPEndPoint(IPAddress.Parse("198.51.100.14"), 443), datagrams, packets);
            Assert.HasCount(16, packets, "the sixteenth datagram ends the read");
            Assert.IsNull(result.DomainName);
            Assert.AreEqual(FilterAction.Block, result.Action);
            Assert.HasCount(15, result.Packets);
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    public void A_QUIC_ClientHello_that_is_not_well_formed_is_dropped()
    {
        using var service = CreateService("blocked.example");
        var datagrams = new TestQuicClient().BuildInitials(BuildMalformedHandshake("www.blocked.example"));
        var packets = new List<IpPacket>();
        try {
            // whole by its length, so the read ended, and with no name the reader could find: it passed as a
            // connection that names no server, where its name is not known
            var result = FeedQuic(service, new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50013),
                new IPEndPoint(IPAddress.Parse("198.51.100.15"), 443), datagrams, packets);
            Assert.IsFalse(result.NeedMore);
            Assert.AreEqual(FilterAction.Block, result.Action);
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    public async Task A_ClientHello_that_is_not_well_formed_fails()
    {
        using var service = CreateService("blocked.example");
        var handshake = BuildMalformedHandshake("www.blocked.example");
        byte[] clientHello = [0x16, 0x03, 0x01, .. Length(handshake.Length), .. handshake];
        await using var stream = new PiecewiseStream(clientHello);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            service.ProcessStream(stream, RemoteEndPoint, TestCt));
    }

    [TestMethod]
    public void A_blocked_QUIC_connection_is_dropped_alone_with_its_early_data()
    {
        using var service = CreateService("blocked.example");
        var allowedClient = new TestQuicClient();
        var blockedClient = new TestQuicClient();
        var source = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50014);
        var destination = new IPEndPoint(IPAddress.Parse("198.51.100.16"), 443);
        var packets = new List<IpPacket>();
        try {
            Assert.AreEqual(FilterAction.Default, FeedQuic(service, source, destination,
                allowedClient.BuildInitials(BuildHandshake("www.allowed.example")), packets).Action);

            // another connection from the same socket, to a blocked name on the same address, while the first lives
            Assert.AreEqual(FilterAction.Block, FeedQuic(service, source, destination,
                blockedClient.BuildInitials(BuildHandshake("www.blocked.example")), packets).Action);

            // Its early data, which names its connection as its Initials do, is dropped with them, and the first
            // connection goes on: the block took the whole flow, and ended it too
            Assert.AreEqual(FilterAction.Block,
                FeedQuic(service, source, destination, [blockedClient.BuildEarlyData()], packets).Action);
            Assert.AreEqual(FilterAction.Default,
                FeedQuic(service, source, destination, [BuildQuicShortHeaderDatagram()], packets).Action);
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    public void A_read_holds_no_more_packets_than_its_limit()
    {
        using var service = CreateService("blocked.example");
        var datagrams = new TestQuicClient().BuildInitials(BuildHandshake("www.allowed.example"), datagramCount: 2);
        var source = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50015);
        var destination = new IPEndPoint(IPAddress.Parse("198.51.100.17"), 443);
        var packets = new List<IpPacket>();
        try {
            // The rest of the ClientHello never comes, and the app goes on sending. The packets wait for the flow's
            // verdict with the read's own, and it held every one of them until its time was out
            Assert.IsTrue(FeedQuic(service, source, destination, [datagrams[0]], packets).NeedMore);
            for (var i = 0; i < 63; i++)
                Assert.IsTrue(FeedQuic(service, source, destination, [BuildQuicShortHeaderDatagram()], packets).NeedMore);

            var result = FeedQuic(service, source, destination, [BuildQuicShortHeaderDatagram()], packets);
            Assert.IsFalse(result.NeedMore);
            Assert.AreEqual(FilterAction.Block, result.Action);
            Assert.HasCount(64, result.Packets);
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    public async Task A_name_that_cannot_be_read_fails_nothing_where_the_filter_holds_no_rule()
    {
        // the service is on for its log alone: no name could change the verdict, so one not read is no reason to
        // end a connection
        using var service = new DomainFilteringService(new StaticDomainFilter(nextFilter: null), new EventId(),
            tlsBufferSize: 4096) { IsEnabled = true };

        // a ClientHello that its stream ends early failed the call
        var clientHello = BuildClientHello("www.any.example")[..40];
        await using var stream = new PiecewiseStream(clientHello);
        var streamResult = await service.ProcessStream(stream, RemoteEndPoint, TestCt);
        Assert.AreEqual(FilterAction.Default, streamResult.Action);
        CollectionAssert.AreEqual(clientHello, streamResult.ReadData.ToArray());

        // and a QUIC one that is not well formed was dropped
        var packets = new List<IpPacket>();
        try {
            var result = FeedQuic(service, new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50016),
                new IPEndPoint(IPAddress.Parse("198.51.100.18"), 443),
                new TestQuicClient().BuildInitials(BuildMalformedHandshake("www.any.example")), packets);
            Assert.IsFalse(result.NeedMore);
            Assert.AreEqual(FilterAction.Default, result.Action);
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    public async Task A_blocked_QUIC_connection_does_not_end_another_on_its_flow()
    {
        if (!QuicConnection.IsSupported)
            Assert.Inconclusive("This machine has no QUIC to make a real Initial packet with.");

        using var service = CreateService("blocked.example");
        var allowedDatagrams = await CaptureQuicInitial("www.allowed.example");
        var blockedDatagrams = await CaptureQuicInitial("www.blocked.example");
        var source = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50008);
        var destination = new IPEndPoint(IPAddress.Parse("198.51.100.10"), 443);
        var packets = new List<IpPacket>();
        try {
            Assert.AreEqual(FilterAction.Default, FeedQuic(service, source, destination, allowedDatagrams, packets).Action);

            // The client opens another connection from the same socket while the first lives, to a blocked name on
            // the same address. It is dropped at its first packets, and never gets past them
            Assert.AreEqual(FilterAction.Block, FeedQuic(service, source, destination, blockedDatagrams, packets).Action);

            // The first connection goes on: the block took the whole flow, and ended it with the blocked one
            Assert.AreEqual(FilterAction.Default,
                FeedQuic(service, source, destination, [BuildQuicShortHeaderDatagram()], packets).Action);

            // the blocked connection's first packets, sent again, are dropped each time, and the flow stays as it is
            Assert.AreEqual(FilterAction.Block, FeedQuic(service, source, destination, blockedDatagrams, packets).Action);
            Assert.AreEqual(FilterAction.Default,
                FeedQuic(service, source, destination, [BuildQuicShortHeaderDatagram()], packets).Action);
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    [DataRow("www.included.example", "www.excluded.example", FilterAction.Include, FilterAction.Include)]
    [DataRow("www.excluded.example", "www.included.example", FilterAction.Include, FilterAction.Include)]
    [DataRow("www.unnamed.example", "www.excluded.example", FilterAction.Default, FilterAction.Default)]
    [DataRow("www.excluded.example", "www.unnamed.example", FilterAction.Default, FilterAction.Default)]
    [DataRow("www.excluded.example", "ftp.excluded.example", FilterAction.Exclude, FilterAction.Exclude)]
    // a name with no entry is judged by the IP rules at its first packets, whose block must hold it there, even on
    // an included flow
    [DataRow("www.included.example", "www.unnamed.example", FilterAction.Default, FilterAction.Include)]
    public async Task A_flow_of_two_QUIC_connections_takes_the_more_protective_verdict(string firstName,
        string secondName, FilterAction expectedConnectionAction, FilterAction expectedFlowAction)
    {
        if (!QuicConnection.IsSupported)
            Assert.Inconclusive("This machine has no QUIC to make a real Initial packet with.");

        using var service = new DomainFilteringService(
            new StaticDomainFilter(nextFilter: null) { Includes = ["included.example"], Excludes = ["excluded.example"] },
            new EventId(), tlsBufferSize: 4096) { IsEnabled = true };
        var firstDatagrams = await CaptureQuicInitial(firstName);
        var secondDatagrams = await CaptureQuicInitial(secondName);
        var source = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50009);
        var destination = new IPEndPoint(IPAddress.Parse("198.51.100.11"), 443);
        var packets = new List<IpPacket>();
        try {
            FeedQuic(service, source, destination, firstDatagrams, packets);

            // Past their first packets the two connections are not told apart, so one's packets may get the other's
            // verdict. The flow took the second's, and with an exclude it sent the first's packets around the tunnel
            var result = FeedQuic(service, source, destination, secondDatagrams, packets);
            Assert.AreEqual(secondName, result.DomainName);
            Assert.AreEqual(expectedConnectionAction, result.Action);
            Assert.AreEqual(expectedFlowAction,
                FeedQuic(service, source, destination, [BuildQuicShortHeaderDatagram()], packets).Action);

            // the second connection's first packets, sent again, leave as they did
            Assert.AreEqual(expectedConnectionAction,
                FeedQuic(service, source, destination, secondDatagrams, packets).Action);
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task A_QUIC_ClientHello_left_unfinished_is_read_again_when_sent_again(int firstPartIndex)
    {
        using var service = CreateService("blocked.example");
        var datagrams = new TestQuicClient().BuildInitials(BuildHandshake("www.allowed.example"), datagramCount: 2);
        var source = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 50010);
        var destination = new IPEndPoint(IPAddress.Parse("198.51.100.12"), 443);
        var packets = new List<IpPacket>();
        try {
            // One part of the ClientHello comes alone, the first, or the second, as when the other is lost or a read
            // begins midway. The client sends both again once its own wait is out, a second by the standard: the
            // read's time was out by then, so the first of them ended it as a block, which was kept for the
            // connection, and each resend after it found the same
            Assert.IsTrue(FeedQuic(service, source, destination, [datagrams[firstPartIndex]], packets).NeedMore);
            await Task.Delay(TimeSpan.FromSeconds(1.2), TestCt);
            var result = FeedQuic(service, source, destination, datagrams, packets);
            Assert.AreEqual("www.allowed.example", result.DomainName);
            Assert.AreEqual(FilterAction.Default, result.Action);
            Assert.AreSame(packets[1], result.Packets.Single(),
                "the first part sent again, held until the second came; the part held before is given up");
        }
        finally {
            foreach (var packet in packets)
                packet.Dispose();
        }
    }
}
