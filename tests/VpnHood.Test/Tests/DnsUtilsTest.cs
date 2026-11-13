using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Utils;

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
