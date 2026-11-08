using System.Net;
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

    // the query for vpnhood.com, with one A record that points back at its name
    private static byte[] BuildResponse(ushort flags)
    {
        var query = DnsUtils.BuildDnsQuery(QueryId, "vpnhood.com");
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
