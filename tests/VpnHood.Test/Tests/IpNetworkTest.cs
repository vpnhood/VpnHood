using System.Net;
using System.Net.Sockets;
using System.Numerics;
using VpnHood.Net.Toolkit.Net;

// ReSharper disable StringLiteralTypo
namespace VpnHood.Test.Tests;

[TestClass]
public class IpNetworkTest : TestBase
{
    [TestMethod]
    [DataRow("10.0.0.0/8/garbage")]
    [DataRow("10.0.0.0/-1")]
    [DataRow("10.0.0.0/+8")]
    [DataRow("10.0.0.0/ 8")]
    [DataRow("10.0.0.0/33")]
    [DataRow("::/129")]
    [DataRow("10.0.0.0")]
    [DataRow("x/8")]
    public void A_text_that_is_no_network_is_refused(string value)
    {
        Assert.ThrowsExactly<FormatException>(() => IpNetwork.Parse(value));
    }

    [TestMethod]
    public void A_prefix_length_or_a_number_beyond_the_family_is_refused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new IpNetwork(IPAddress.Parse("10.0.0.0"), 33));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new IpNetwork(IPAddress.Parse("10.0.0.0"), -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new IpNetwork(IPAddress.IPv6Any, 129));
        Assert.AreEqual(IPAddress.Parse("10.0.0.7"), new IpNetwork(IPAddress.Parse("10.0.0.7"), 32).LastIpAddress);
        Assert.AreEqual(IPAddress.Parse("255.255.255.255"), IpNetwork.Parse("0.0.0.0/0").LastIpAddress);

        // a number above the family's last address is no address of it
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            IPAddressUtil.FromBigInteger(BigInteger.One << 32, AddressFamily.InterNetwork));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            IPAddressUtil.FromBigInteger(BigInteger.MinusOne, AddressFamily.InterNetwork));
        Assert.AreEqual(IPAddress.Parse("255.255.255.255"),
            IPAddressUtil.FromBigInteger((BigInteger.One << 32) - 1, AddressFamily.InterNetwork));
    }

    [TestMethod]
    public void A_bad_range_or_address_is_a_bad_argument()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new IpRange(IPAddress.Parse("10.0.0.9"), IPAddress.Parse("10.0.0.1")));
        Assert.ThrowsExactly<ArgumentException>(() => new IpRange(IPAddress.Parse("10.0.0.9"), IPAddress.Parse("aa::1")));
        Assert.ThrowsExactly<ArgumentException>(() => IPAddressUtil.ToLong(IPAddress.IPv6Loopback));
    }

    // the list's own order comes first: its ranges never start at the same address, so a key has nothing to order
    [TestMethod]
    public void ThenBy_keeps_the_lists_order()
    {
        var list = new IpRangeOrderedList([
            IpRange.Parse("10.0.0.0-10.0.0.9"), IpRange.Parse("1.0.0.0-1.0.0.255"), IpRange.Parse("5.0.0.0")
        ]);

        // by their sizes they would come another way round
        CollectionAssert.AreEqual(list.ToArray(), list.ThenBy(x => x.Total).ToArray());
        CollectionAssert.AreEqual(list.ToArray(), list.ThenByDescending(x => x.Total).ToArray());
    }

    [TestMethod]
    public void A_range_and_a_network_leave_the_scope_id_out()
    {
        var scoped = IPAddress.Parse("::%3");
        var range = new IpRange(scoped, IPAddress.Parse("::5%7"));
        Assert.AreEqual(0, range.FirstIpAddress.ScopeId);
        Assert.AreEqual(0, range.LastIpAddress.ScopeId);
        Assert.AreEqual(3, scoped.ScopeId); // the caller's own address is as it was
        Assert.AreEqual(0, new IpNetwork(IPAddress.Parse("fe80::1%4"), 64).Prefix.ScopeId);
        Assert.IsTrue(IPAddressUtil.IsMinValue(scoped));
        Assert.IsTrue(IPAddressUtil.IsMaxValue(IPAddress.Parse("ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff%2")));

        // excluding :: by an address with a scope id leaves no :: behind
        var rest = new IpRangeOrderedList([new IpRange(IPAddressUtil.MinIPv6Value, IPAddress.Parse("::9"))])
            .Exclude(scoped);
        Assert.IsFalse(rest.Contains(IPAddress.IPv6Any));
        Assert.AreEqual(IPAddress.Parse("::1"), rest[0].FirstIpAddress);
    }

    [TestMethod]
    public void Ranges_are_written_and_read_without_closing_the_stream()
    {
        var list = new IpRangeOrderedList([IpRange.Parse("1.0.0.0-1.0.0.255"), IpRange.Parse("aa::1-aa::ff")]);
        using var stream = new MemoryStream();

        list.Serialize(stream);
        Assert.IsTrue(stream.CanWrite);

        stream.Position = 0;
        CollectionAssert.AreEqual(list.ToArray(), IpRangeOrderedList.Deserialize(stream).ToArray());
        Assert.IsTrue(stream.CanRead);

        stream.Position = 0;
        Assert.HasCount(2, IpRangeOrderedList.DeserializeRaw(stream).ToArray());
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public void Ranges_that_are_cut_short_or_malformed_are_refused()
    {
        using var stream = new MemoryStream();
        new IpRangeOrderedList([IpRange.Parse("aa::1-aa::ff")]).Serialize(stream);
        var bytes = stream.ToArray();

        // the last address cut to 4 of its 16 bytes: no IPv4 address, but a list that ends early
        Assert.ThrowsExactly<EndOfStreamException>(() =>
            IpRangeOrderedList.Deserialize(new MemoryStream(bytes[..^12])));
        Assert.ThrowsExactly<EndOfStreamException>(() =>
            IpRangeOrderedList.DeserializeRaw(new MemoryStream(bytes[..^12])).ToArray());

        // a negative count, and an address that is neither 4 nor 16 bytes long
        Assert.ThrowsExactly<InvalidDataException>(() =>
            IpRangeOrderedList.Deserialize(new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF])));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            IpRangeOrderedList.Deserialize(new MemoryStream([1, 0, 0, 0, 5, 1, 2, 3, 4, 5, 5, 1, 2, 3, 4, 5])));

        // a count far above what the stream holds is found out by the stream's end, not by the memory it asks for
        Assert.ThrowsExactly<EndOfStreamException>(() =>
            IpRangeOrderedList.Deserialize(new MemoryStream([0xFF, 0xFF, 0xFF, 0x7F])));
    }

    [TestMethod]
    public void Union_reads_its_ranges_once()
    {
        var list = new IpRangeOrderedList([IpRange.Parse("1.0.0.0")]);
        var reads = 0;

        Assert.HasCount(2, list.Union(GetRanges()));
        Assert.AreEqual(1, reads);
        return;

        IEnumerable<IpRange> GetRanges()
        {
            reads++;
            yield return IpRange.Parse("2.0.0.0");
        }
    }

    [TestMethod]
    public void A_mapped_address_has_its_IPv4_addresss_dos_key()
    {
        Assert.AreEqual("10.0.0.5", IPAddressUtil.GetDosKey(IPAddress.Parse("::ffff:10.0.0.5")));
        Assert.AreEqual("10.0.0.5", IPAddressUtil.GetDosKey(IPAddress.Parse("10.0.0.5")));
        Assert.AreEqual("2001:db8:1:2::/64", IPAddressUtil.GetDosKey(IPAddress.Parse("2001:db8:1:2:3:4:5:6")));
    }

    // the token is canceled before the call, so nothing is sent
    [TestMethod]
    public async Task A_public_address_lookup_its_caller_cancels_is_canceled()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork, userAgent: null, canceled.Token));
    }

    [TestMethod]
    public void Invert_Unify_Convert()
    {
        var ipRangesSorted = new[] {
            IpRange.Parse("127.0.0.0 - 127.255.255.255"),
            IpRange.Parse("192.168.0.0 - 192.168.255.255"),
            IpRange.Parse("9A::0000 - AA::FFFF")
        };

        var ipRanges = new[] {
            IpRange.Parse("AA::0000 - AA::FCFC"),
            IpRange.Parse("192.168.0.0 - 192.168.255.140"),
            IpRange.Parse("9A::0000 - AA::AABB"),
            IpRange.Parse("AA::0000 - AA::FFF0"),
            IpRange.Parse("AA::FFF1 - AA::FFFF"),
            IpRange.Parse("192.168.10.0 - 192.168.255.255"),
            IpRange.Parse("127.0.0.0 - 127.255.255.255"),
            IpRange.Parse("127.0.0.0 - 127.255.255.254") //extra
        }.ToOrderedList();
        CollectionAssert.AreEqual(ipRangesSorted, ipRanges.ToArray());

        var expected = new[] {
            IpRange.Parse("0.0.0.0 - 126.255.255.255"),
            IpRange.Parse("128.0.0.0 - 192.167.255.255"),
            IpRange.Parse("192.169.0.0 - 255.255.255.255"),
            IpRange.Parse(":: - 99:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF"),
            IpRange.Parse("AA::01:0000 - FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF")
        }.ToOrderedList();

        CollectionAssert.AreEqual(expected.ToArray(), ipRanges.Invert().ToArray());

        // check network
        CollectionAssert.AreEqual(expected.ToIpNetworks().ToArray(), ipRanges.Invert().ToIpNetworks().ToArray());
        CollectionAssert.AreEqual(ipRangesSorted, ipRanges.ToArray());
    }

    [TestMethod]
    public void DeserializeRaw_matches_Deserialize()
    {
        var ranges = new[] {
            IpRange.Parse("1.1.1.1 - 1.1.1.10"),
            IpRange.Parse("9.9.9.0 - 9.9.9.255"),
            IpRange.Parse("192.168.0.0 - 192.168.255.255"),
            IpRange.Parse("AA::0000 - AA::FFFF"),
            IpRange.Parse("2001:db8:: - 2001:db8::ffff")
        }.ToOrderedList();

        using var ms = new MemoryStream();
        ranges.Serialize(ms);
        var bytes = ms.ToArray(); // MemoryStream.ToArray() is valid even after Serialize disposes the stream

        // object path (allocates IpRange/IPAddress, sorts+unifies)
        var deserialized = IpRangeOrderedList.Deserialize(new MemoryStream(bytes)).ToArray();

        // raw streaming path (no object graph, no sort) — reconstruct to compare
        var raw = IpRangeOrderedList.DeserializeRaw(new MemoryStream(bytes))
            .Select(x => new IpRange(new IPAddress(x.Start), new IPAddress(x.End)))
            .ToArray();

        CollectionAssert.AreEqual(deserialized, raw);

        // family is carried by byte length (4 = IPv4, 16 = IPv6) — what the SQLite builder keys on
        var lengths = IpRangeOrderedList.DeserializeRaw(new MemoryStream(bytes))
            .Select(x => x.Start.Length).ToArray();
        Assert.IsTrue(lengths.Contains(4), "expected some IPv4 ranges");
        Assert.IsTrue(lengths.Contains(16), "expected some IPv6 ranges");
    }

    [TestMethod]
    public void IpNetwork_Unit()
    {
        var ipNetwork = IpNetwork.Parse("192.168.23.23/32");
        var inverted = ipNetwork.Invert().ToArray();
        Assert.HasCount(32, inverted);
        CollectionAssert.AreEqual(new[] { ipNetwork },
            inverted.ToIpRanges().Invert(true, false).ToIpNetworks().ToArray());

        ipNetwork = IpNetwork.AllV4;
        Assert.IsEmpty(ipNetwork.Invert().ToArray());

        ipNetwork = IpNetwork.AllV6;
        Assert.IsEmpty(ipNetwork.Invert().ToArray());

        CollectionAssert.AreEqual(IpNetwork.All.ToArray(),
            Array.Empty<IpNetwork>().ToIpRanges().Invert().ToIpNetworks().ToArray());
    }

    [TestMethod]
    public void IpRange_IsInRange()
    {
        // simple
        var range = new IpRange(IPAddress.Parse("1.1.1.1"), IPAddress.Parse("1.1.1.10"));
        Assert.IsTrue(range.IsInRange(IPAddress.Parse("1.1.1.5")));
        Assert.IsFalse(range.IsInRange(IPAddress.Parse("1.1.1.12")));


        // array
        var ipRanges = new[] {
            IpRange.Parse("9.9.9.9 - 9.9.9.9"),
            IpRange.Parse("8.8.8.8"),
            IpRange.Parse("3.3.3.3 - 4.4.4.4"),
            IpRange.Parse("3.3.3.3 - 4.4.3.4"),
            IpRange.Parse("3.3.3.3 - 4.4.2.4"),
            IpRange.Parse("FF:: - FF::FF"),
            IpRange.Parse("EF:: - FF::FF"),
            IpRange.Parse("5.5.5.5-5.5.5.10")
        };

        var ipRangeOrderedList = ipRanges.ToOrderedList();
        Assert.IsFalse(ipRangeOrderedList.Contains(IPAddress.Parse("9.9.9.7")));
        Assert.IsTrue(ipRangeOrderedList.Contains(IPAddress.Parse("8.8.8.8")));
        Assert.IsTrue(ipRangeOrderedList.Contains(IPAddress.Parse("9.9.9.9")));
        Assert.IsFalse(ipRangeOrderedList.Contains(IPAddress.Parse("4.4.4.5")));
        Assert.IsTrue(ipRangeOrderedList.Contains(IPAddress.Parse("4.4.4.3")));
        Assert.IsTrue(ipRangeOrderedList.Contains(IPAddress.Parse("FF::F0")));
        Assert.IsFalse(ipRangeOrderedList.Contains(IPAddress.Parse("AF::F0")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IpRange_Intersect(bool swap)
    {
        var ipRanges1 = new[] {
            IpRange.Parse("AA::FFF5 - AA::FFFF"),
            IpRange.Parse("192.168.10.0 - 192.168.12.12"),
            IpRange.Parse("190.190.10.0 - 190.190.11.0"), //ignore
            IpRange.Parse("30.30.10.50 - 30.30.10.100"),
            IpRange.Parse("20.20.10.50 - 20.20.10.55"),
            IpRange.Parse("20.20.10.60 - 20.20.10.100")
        }.ToOrderedList();

        var ipRanges2 = new[] {
            IpRange.Parse("AA::FFF1 - AA::FFF6"),
            IpRange.Parse("192.168.10.0 - 192.168.255.255"),
            IpRange.Parse("190.190.11.1 - 190.190.11.50"), //ignore
            IpRange.Parse("30.30.10.70 - 30.30.10.110"),
            IpRange.Parse("20.20.10.0 - 20.20.10.90")
        }.ToOrderedList();

        // Expected
        // AA::FFF5 - AA::FFF6
        var ranges = swap
            ? ipRanges2.Intersect(ipRanges1)
            : ipRanges1.Intersect(ipRanges2);

        var i = 0;
        Assert.AreEqual("20.20.10.50-20.20.10.55", ranges[i++].ToString().ToUpper());
        Assert.AreEqual("20.20.10.60-20.20.10.90", ranges[i++].ToString().ToUpper());
        Assert.AreEqual("30.30.10.70-30.30.10.100", ranges[i++].ToString().ToUpper());
        Assert.AreEqual("192.168.10.0-192.168.12.12", ranges[i++].ToString().ToUpper());
        Assert.AreEqual("AA::FFF5-AA::FFF6", ranges[i++].ToString().ToUpper());
        Assert.AreEqual(i, ranges.Count);
    }

    [TestMethod]
    public void IpRangeOrderedList_Contains_searches_mapped_and_mixed_ranges()
    {
        // an IPv4-mapped range is not merged into the IPv4 range it overlaps, yet both are searched as IPv4, and a
        // range may run from a mapped address to a plain IPv6 one
        var list = new[] {
            IpRange.Parse("10.0.0.0 - 10.0.0.255"),
            IpRange.Parse("::ffff:10.0.0.10 - ::ffff:10.0.0.20"),
            IpRange.Parse("10.0.1.0 - 10.0.1.255"),
            IpRange.Parse("::ffff:10.0.2.0 - ::ffff:10.0.2.255"),
            IpRange.Parse("::ffff:192.168.0.0 - 2001::1"),
            IpRange.Parse("2001:db8:: - 2001:db8::ffff")
        }.ToOrderedList();

        string[] inside = [
            "10.0.0.0", "10.0.0.15", "10.0.0.100", "10.0.0.255", "::ffff:10.0.0.100", "10.0.1.7", "10.0.2.1",
            "::ffff:10.0.2.1", "192.168.0.1", "255.255.255.255", "::1", "2001::1", "2001:db8::1", "2001:db8::ffff",
            "2001:db8::1%3"
        ];
        string[] outside = [
            "9.255.255.255", "::ffff:9.255.255.255", "10.0.3.0", "192.167.255.255", "2001::2", "2001:db8::1:0", "fe80::1"
        ];

        foreach (var text in inside)
            AssertContains(list, IPAddress.Parse(text), true);

        foreach (var text in outside)
            AssertContains(list, IPAddress.Parse(text), false);
    }

    [TestMethod]
    public void IpRangeOrderedList_Contains_agrees_with_its_ranges()
    {
        // random ranges that overlap across IPv4, IPv4-mapped and IPv6, and random addresses near them
        var random = new Random(7);
        for (var round = 0; round < 300; round++) {
            var list = Enumerable.Range(0, random.Next(1, 12)).Select(_ => RandomRange(random)).ToOrderedList();
            for (var i = 0; i < 50; i++) {
                var ipAddress = RandomAddress(random);
                AssertContains(list, ipAddress, list.Any(x => x.IsInRange(ipAddress)));
            }
        }
    }

    // Every way to ask, against the expected answer.
    private static void AssertContains(IpRangeOrderedList list, IPAddress ipAddress, bool expected)
    {
        Assert.AreEqual(expected, list.Any(x => x.IsInRange(ipAddress)), $"a range of the list holds {ipAddress}");
        Assert.AreEqual(expected, list.Any(x => x.IsInRange(ipAddress.ToValue())), $"IsInRange of {ipAddress}'s value");
        Assert.AreEqual(expected, list.Contains(ipAddress), $"Contains({ipAddress})");
        Assert.AreEqual(expected, list.Contains(ipAddress.ToValue()), $"Contains of {ipAddress}'s value");
    }

    // A range of IPv4, IPv4-mapped or IPv6 addresses, or from a mapped address to a plain IPv6 one.
    private static IpRange RandomRange(Random random)
    {
        var kind = random.Next(4);
        var first = RandomAddress(random, kind == 3 ? 1 : kind);
        var last = RandomAddress(random, kind == 3 ? 2 : kind);
        return IPAddressUtil.Compare(first, last) <= 0 ? new IpRange(first, last) : new IpRange(last, first);
    }

    private static IPAddress RandomAddress(Random random) => RandomAddress(random, random.Next(3));

    // 0: IPv4 in 10.0.0.0/16, 1: the same as IPv4-mapped, 2: IPv6 in 2001:db8::/112.
    private static IPAddress RandomAddress(Random random, int kind)
    {
        var bytes = new byte[16];
        bytes[14] = (byte)random.Next(256);
        bytes[15] = (byte)random.Next(256);
        switch (kind) {
            case 0:
                return new IPAddress([10, 0, bytes[14], bytes[15]]);

            case 1:
                bytes[10] = 0xFF;
                bytes[11] = 0xFF;
                bytes[12] = 10;
                return new IPAddress(bytes);

            default:
                bytes[0] = 0x20;
                bytes[1] = 0x01;
                bytes[2] = 0x0d;
                bytes[3] = 0xb8;
                return new IPAddress(bytes);
        }
    }
}