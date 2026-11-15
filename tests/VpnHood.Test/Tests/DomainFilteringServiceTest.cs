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

namespace VpnHood.Test.Tests;

[TestClass]
public class DomainFilteringServiceTest : TestBase
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Parse("198.51.100.1"), 443);

    // gives its pieces one at a read, as a connection gives what has arrived so far
    private sealed class PiecewiseStream(params byte[][] pieces) : Stream
    {
        private int _pieceIndex;
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

            var piece = pieces[_pieceIndex++];
            piece.CopyTo(buffer, offset);
            return piece.Length;
        }

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

    // a TLS record that holds a ClientHello with a server name and nothing else in its extensions
    private static byte[] BuildClientHello(string serverName)
    {
        var name = Encoding.ASCII.GetBytes(serverName);
        byte[] serverNameExtension = [
            0x00, 0x00, .. Length(name.Length + 5), .. Length(name.Length + 3), 0x00, .. Length(name.Length), .. name
        ];
        byte[] body = [
            0x03, 0x03, .. new byte[32], 0x00, 0x00, 0x02, 0x13, 0x01, 0x01, 0x00,
            .. Length(serverNameExtension.Length), .. serverNameExtension
        ];
        byte[] handshake = [0x01, 0x00, .. Length(body.Length), .. body];
        return [0x16, 0x03, 0x01, .. Length(handshake.Length), .. handshake];
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
    // hold the name. Nothing answers, so the connection is given up once they are in.
    private async Task<IReadOnlyList<byte[]>> CaptureQuicInitial(string serverName)
    {
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
                ApplicationProtocols = [SslApplicationProtocol.Http3]
            }
        }, connectCts.Token).AsTask();

        // a ClientHello with a large key share takes two datagrams, sent together, well before any is sent again
        var datagrams = new List<byte[]>();
        var first = await udpClient.ReceiveAsync(TestCt).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        datagrams.Add(first.Buffer);
        try {
            var second = await udpClient.ReceiveAsync(TestCt).AsTask().WaitAsync(TimeSpan.FromMilliseconds(200), TestCt);
            datagrams.Add(second.Buffer);
        }
        catch (TimeoutException) {
            // one datagram held it all
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
}
