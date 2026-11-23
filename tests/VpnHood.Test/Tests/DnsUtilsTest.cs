using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// A response gives addresses only whole and without an error: a truncated one may lack some of the name's addresses,
// and a check whether an address is still the name's would take its absence for a move.
[TestClass]
public class DnsUtilsTest
{
    private const ushort QueryId = 0x1234;
    private const ushort WholeResponse = 0x8180; // QR, RD, RA
    private const ushort TruncatedResponse = 0x8380; // QR, TC, RD, RA
    private const ushort ServerFailure = 0x8182; // QR, RD, RA, RCODE 2
    private static readonly IPAddress Address = IPAddress.Parse("192.0.2.1");
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public void A_whole_response_gives_its_addresses()
    {
        var hostEntry = DnsUtils.ParseDnsResponse(BuildResponse(WholeResponse), QueryId);

        CollectionAssert.AreEqual(new[] { Address }, hostEntry.AddressList);
    }

    [TestMethod]
    public void A_truncated_response_gives_none()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DnsUtils.ParseDnsResponse(BuildResponse(TruncatedResponse), QueryId));
    }

    [TestMethod]
    public void A_response_with_an_error_gives_none()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DnsUtils.ParseDnsResponse(BuildResponse(ServerFailure), QueryId));
    }

    [TestMethod]
    public void A_query_carries_the_name_without_its_last_dot_and_in_ascii()
    {
        CollectionAssert.AreEqual(
            DnsUtils.BuildDnsQuery(QueryId, "vpnhood.com"), DnsUtils.BuildDnsQuery(QueryId, "vpnhood.com."));
        CollectionAssert.AreEqual(
            DnsUtils.BuildDnsQuery(QueryId, "xn--bcher-kva.de"), DnsUtils.BuildDnsQuery(QueryId, "b\u00FCcher.de"));
    }

    [TestMethod]
    [DataRow("a..com")]
    [DataRow(".com")]
    [DataRow("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.com")] // a label of 64
    public void A_name_that_is_no_host_name_makes_no_query(string host)
    {
        Assert.ThrowsExactly<ArgumentException>(() => DnsUtils.BuildDnsQuery(QueryId, host));
    }

    [TestMethod]
    public void A_response_that_ends_early_is_refused_as_such()
    {
        var response = BuildResponse(WholeResponse);
        Assert.ThrowsExactly<EndOfStreamException>(() => DnsUtils.ParseDnsResponse(response[..^2], QueryId));
    }

    // the query's own client is of the resolver's family: made without one it is IPv4, and reaches no IPv6 resolver
    [TestMethod]
    public async Task A_resolver_at_an_IPv6_address_is_asked()
    {
        if (!Socket.OSSupportsIPv6)
            Assert.Inconclusive("This machine has no IPv6.");

        var cancellationToken = TestContext.CancellationToken;
        using var resolver = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        var resolverEndPoint = (IPEndPoint?)resolver.Client.LocalEndPoint ??
                               throw new InvalidOperationException("The resolver has no endpoint.");
        var resolverTask = Task.Run(async () => {
            var request = await resolver.ReceiveAsync(cancellationToken);
            await resolver.SendAsync(BuildResponse(request.Buffer, WholeResponse), request.RemoteEndPoint,
                cancellationToken);
        }, cancellationToken);

        var hostEntry = await DnsUtils.GetHostEntry("vpnhood.com", resolverEndPoint, TimeSpan.FromSeconds(5),
            cancellationToken);
        await resolverTask;
        CollectionAssert.AreEqual(new[] { Address }, hostEntry.AddressList);
    }

    // Under the kill switch the client asked the network's resolvers for A records alone, so a server name with IPv6
    // addresses alone resolved to nothing. They are asked for its AAAA records too
    [TestMethod]
    public async Task A_name_with_IPv6_addresses_alone_resolves_over_the_networks_resolvers()
    {
        var cancellationToken = TestContext.CancellationToken;
        var ipv6Address = IPAddress.Parse("2001:db8::1");
        using var resolver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var resolverEndPoint = (IPEndPoint?)resolver.Client.LocalEndPoint ??
                               throw new InvalidOperationException("The resolver has no endpoint.");

        // the A query gets no record, the AAAA query the address
        var resolverTask = Task.Run(async () => {
            for (var i = 0; i < 2; i++) {
                var request = await resolver.ReceiveAsync(cancellationToken);
                var isAaaaQuery = request.Buffer[^3] == 28;
                var response = isAaaaQuery
                    ? BuildAnswer(request.Buffer, 28, ipv6Address.GetAddressBytes())
                    : BuildAnswer(request.Buffer, recordType: null, []);

                await resolver.SendAsync(response, request.RemoteEndPoint, cancellationToken);
            }
        }, cancellationToken);

        var addresses = await DnsUtils.GetHostAddresses("vpnhood.com", [resolverEndPoint], new TestSocketFactory(),
            TimeSpan.FromSeconds(5), cancellationToken);

        await resolverTask;
        CollectionAssert.AreEqual(new[] { ipv6Address }, addresses.ToArray());
    }

    // A resolver that drops the AAAA query, as some home routers' do, cost the A addresses it gave with it, and the whole
    // timeout. It gives them, at once
    [TestMethod]
    public async Task A_resolver_that_drops_the_IPv6_query_still_gives_the_IPv4_addresses()
    {
        var cancellationToken = TestContext.CancellationToken;
        using var resolver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var resolverEndPoint = (IPEndPoint?)resolver.Client.LocalEndPoint ??
                               throw new InvalidOperationException("The resolver has no endpoint.");

        // the A query alone is answered
        var resolverTask = Task.Run(async () => {
            for (var i = 0; i < 2; i++) {
                var request = await resolver.ReceiveAsync(cancellationToken);
                if (request.Buffer[^3] == 1)
                    await resolver.SendAsync(BuildAnswer(request.Buffer, 1, Address.GetAddressBytes()),
                        request.RemoteEndPoint, cancellationToken);
            }
        }, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var addresses = await DnsUtils.GetHostAddresses("vpnhood.com", [resolverEndPoint], new TestSocketFactory(),
            TimeSpan.FromSeconds(10), cancellationToken);

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"The lookup took {stopwatch.Elapsed}.");
        CollectionAssert.AreEqual(new[] { Address }, addresses.ToArray());
        await resolverTask.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }

    // An IPv6 answer that came more than 50 ms before the IPv4 one was the lookup's whole result, so a client without
    // IPv6, which drops IPv6 addresses, had no server to try. The IPv4 answer is waited for; two seconds late, as a lookup
    // that waited 50 ms only would have ended long before, however slow the test machine
    [TestMethod]
    public async Task An_IPv6_answer_that_comes_first_keeps_the_IPv4_addresses()
    {
        var cancellationToken = TestContext.CancellationToken;
        var ipv6Address = IPAddress.Parse("2001:db8::1");
        using var resolver = new TestDnsResolver([Address, ipv6Address]) {
            AnswerDelayIpV4 = TimeSpan.FromSeconds(2)
        };

        var addresses = await DnsUtils.GetHostAddresses("vpnhood.com", [resolver.EndPoint], new TestSocketFactory(),
            TimeSpan.FromSeconds(10), cancellationToken);

        CollectionAssert.AreEqual(new[] { Address, ipv6Address }, addresses.ToArray());
    }

    // A query the lookup no longer waited for kept its client open until its own timeout, which may be none. It ends
    // with the lookup, its client closed by the lookup's end
    [TestMethod]
    public async Task A_query_the_lookup_no_longer_waits_for_ends_with_it()
    {
        var cancellationToken = TestContext.CancellationToken;
        using var resolver = new TestDnsResolver([Address]) {
            AnswerDelayIpV6 = Timeout.InfiniteTimeSpan
        };

        var socketFactory = new RecordingSocketFactory();

        var addresses = await DnsUtils.GetHostAddresses("vpnhood.com", [resolver.EndPoint], socketFactory,
            Timeout.InfiniteTimeSpan, cancellationToken);

        CollectionAssert.AreEqual(new[] { Address }, addresses.ToArray());
        Assert.HasCount(2, socketFactory.UdpClients);
        Assert.IsTrue(socketFactory.UdpClients.All(IsClosed), "A query's client is still open.");
    }

    // A lookup canceled as it waited for the IPv4 answer threw once that query had ended, the IPv6 one still ending, its
    // client open. Both queries end, their clients closed, before a canceled lookup throws: the clients are looked at as
    // the lookup ends, on the thread that ends it. The IPv6 query's client, made second, closes once the lookup has ended,
    // or after 3 seconds where it has not, as a lookup that waits for it cannot end first: a lookup that does not wait
    // ends with that client open, however long the IPv4 query takes to end, and no thread a cancel runs on is held for good
    [TestMethod]
    public async Task A_canceled_lookup_ends_both_queries_first()
    {
        using var resolver = new TestDnsResolver([Address]) {
            AnswerDelayIpV4 = Timeout.InfiniteTimeSpan,
            AnswerDelayIpV6 = Timeout.InfiniteTimeSpan
        };

        var lookupEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var socketFactory = new RecordingSocketFactory {
            LaterClose = () => _ = lookupEnded.Task.Wait(TimeSpan.FromSeconds(3), CancellationToken.None)
        };

        using var lookupCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        var lookup = DnsUtils.GetHostAddresses("vpnhood.com", [resolver.EndPoint], socketFactory,
            Timeout.InfiniteTimeSpan, lookupCts.Token);

        var isClosedAtEnd = lookup.ContinueWith(_ => {
            var isClosed = socketFactory.UdpClients.All(IsClosed);
            lookupEnded.TrySetResult();
            return isClosed;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        await VhTestUtil.AssertEqualsWait(2, () => resolver.Queries.Count, "the lookup's queries",
            cancellationToken: TestContext.CancellationToken);

        await lookupCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => lookup);
        Assert.HasCount(2, socketFactory.UdpClients);
        Assert.IsTrue(await isClosedAtEnd, "The lookup ended with a query's client still open.");
    }

    // a closed client lets its socket go, or keeps one that is closed
    private static bool IsClosed(UdpClient udpClient)
    {
        if (udpClient.Client is not { } socket)
            return true;

        try {
            _ = socket.Available;
            return false;
        }
        catch (ObjectDisposedException) {
            return true;
        }
    }

    // the system's sockets, with each UDP client it made kept for a look
    private sealed class RecordingSocketFactory : TestSocketFactory
    {
        private readonly List<UdpClient> _udpClients = [];

        public IReadOnlyList<UdpClient> UdpClients {
            get {
                lock (_udpClients)
                    return _udpClients.ToArray();
            }
        }

        // what the close of each client after the first waits for before its socket goes, as a slow one's
        public Action? LaterClose { get; init; }

        public override UdpClient CreateUdpClient(AddressFamily addressFamily)
        {
            lock (_udpClients) {
                var udpClient = _udpClients.Count > 0 && LaterClose is { } laterClose
                    ? new SlowCloseUdpClient(addressFamily, laterClose)
                    : base.CreateUdpClient(addressFamily);

                _udpClients.Add(udpClient);
                return udpClient;
            }
        }
    }

    // a client whose close waits for something before its socket goes
    private sealed class SlowCloseUdpClient(AddressFamily addressFamily, Action beforeClose) : UdpClient(addressFamily)
    {
        protected override void Dispose(bool disposing)
        {
            beforeClose();
            base.Dispose(disposing);
        }
    }

    // the answer to a query: a whole response with one record of the type given, or with none where it is null
    private static byte[] BuildAnswer(byte[] query, ushort? recordType, ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream();
        stream.Write(query);
        stream.Position = 2;
        stream.Write([WholeResponse >> 8, WholeResponse & 0xFF]);
        stream.Position = 6;
        stream.Write([0, (byte)(recordType is null ? 0 : 1)]);
        stream.Position = stream.Length;
        if (recordType is { } type) {
            stream.Write([0xC0, 0x0C, (byte)(type >> 8), (byte)type, 0, 1, 0, 0, 0, 60, 0, (byte)data.Length]);
            stream.Write(data);
        }

        return stream.ToArray();
    }

    // the query for vpnhood.com, with one A record that points back at its name
    private static byte[] BuildResponse(ushort flags)
    {
        return BuildResponse(DnsUtils.BuildDnsQuery(QueryId, "vpnhood.com"), flags);
    }

    private static byte[] BuildResponse(byte[] query, ushort flags)
    {
        using var stream = new MemoryStream();
        stream.Write(query);
        stream.Position = 2;
        stream.Write([(byte)(flags >> 8), (byte)flags]);
        stream.Position = 6;
        stream.Write([0, 1]); // one answer
        stream.Position = stream.Length;
        stream.Write([0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4]); // the name's pointer, A, IN, TTL 60, 4 bytes
        stream.Write(Address.GetAddressBytes());
        return stream.ToArray();
    }
}
