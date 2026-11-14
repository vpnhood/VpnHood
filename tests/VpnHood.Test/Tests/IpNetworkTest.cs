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
    public void A_range_or_a_network_of_mapped_addresses_is_that_of_their_IPv4_addresses()
    {
        var range = IpRange.Parse("::ffff:10.0.0.1 - ::ffff:10.0.0.9");
        Assert.AreEqual(IpRange.Parse("10.0.0.1 - 10.0.0.9"), range);
        Assert.AreEqual(AddressFamily.InterNetwork, range.AddressFamily);
        Assert.AreEqual(new IpRange(IPAddress.Parse("10.0.0.5")), new IpRange(IPAddress.Parse("::ffff:10.0.0.5")));
        Assert.IsTrue(range.IsInRange(IPAddress.Parse("10.0.0.5")));
        Assert.IsTrue(range.IsInRange(IPAddress.Parse("::ffff:10.0.0.5")));

        var network = IpNetwork.Parse("::ffff:10.0.0.0/104");
        Assert.AreEqual(IpNetwork.Parse("10.0.0.0/8"), network);
        Assert.AreEqual("10.0.0.0/8", network.ToString());
        Assert.AreEqual(IpNetwork.Parse("10.0.0.5/32"), new IpNetwork(IPAddress.Parse("::ffff:10.0.0.5")));
        Assert.AreEqual(IpNetwork.AllV4, IpNetwork.Parse("::ffff:0:0/96"));
        Assert.IsTrue(network.Contains(IPAddress.Parse("::ffff:10.9.9.9")));
        CollectionAssert.AreEqual(IpNetwork.Parse("10.0.0.5/32").Invert().ToArray(),
            IpNetwork.Parse("::ffff:10.0.0.5/128").Invert().ToArray());
        CollectionAssert.AreEqual(new[] { IpNetwork.Parse("10.0.0.0/30") },
            IpNetwork.FromRange(IPAddress.Parse("::ffff:10.0.0.0"), IPAddress.Parse("::ffff:10.0.0.3")).ToArray());
    }

    // by number, as any two IPv6 addresses: taken as the IPv4 address it carries, the mapped one came first
    [TestMethod]
    public void A_range_from_a_mapped_address_down_to_a_lower_address_is_refused()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new IpRange(IPAddress.Parse("::ffff:10.0.0.5"), IPAddress.Parse("::1")));

        // up from a lower address it is a range of IPv6 addresses like any other
        var range = new IpRange(IPAddress.Parse("::1"), IPAddress.Parse("::ffff:10.0.0.5"));
        Assert.AreEqual(AddressFamily.InterNetworkV6, range.AddressFamily);
        Assert.IsTrue(range.Total > 0);
        Assert.IsTrue(range.IsInRange(IPAddress.Parse("::2")));
    }

    [TestMethod]
    public void A_network_that_reaches_into_the_mapped_addresses_is_an_IPv6_network_like_any_other()
    {
        var network = IpNetwork.Parse("::/80");
        var range = network.ToIpRange();
        Assert.AreEqual(IPAddress.Parse("::"), range.FirstIpAddress);
        Assert.AreEqual(IPAddress.Parse("::ffff:255.255.255.255"), range.LastIpAddress);
        Assert.IsTrue(network.Contains(IPAddress.Parse("::5")));
        Assert.IsTrue(range.IsInRange(IPAddress.Parse("::5")));

        // a mapped address is asked about as its IPv4 address, which no IPv6 network holds
        Assert.IsFalse(network.Contains(IPAddress.Parse("::ffff:1.2.3.4")));
        Assert.IsFalse(network.Contains(IPAddress.Parse("1.2.3.4")));

        // it comes back from its range, and from two inversions
        CollectionAssert.AreEqual(new[] { network }, new[] { network }.ToIpRanges().ToIpNetworks().ToArray());
        CollectionAssert.AreEqual(new[] { network },
            network.Invert().ToIpRanges().Invert(includeIPv4: false).ToIpNetworks().ToArray());
    }

    [TestMethod]
    public void A_list_takes_mapped_ranges_as_IPv4_ranges()
    {
        // one between two IPv4 ranges it overlaps merges them
        var merged = new[] {
            IpRange.Parse("10.0.0.0 - 10.0.0.100"),
            IpRange.Parse("::ffff:10.0.0.50 - ::ffff:10.0.0.60"),
            IpRange.Parse("10.0.0.90 - 10.0.0.200")
        }.ToOrderedList();
        CollectionAssert.AreEqual(new[] { IpRange.Parse("10.0.0.0 - 10.0.0.200") }, merged.ToArray());

        // an IPv4 list and a mapped address share that address
        var shared = merged.Intersect([new IpRange(IPAddress.Parse("::ffff:10.0.0.5"))]);
        CollectionAssert.AreEqual(new[] { IpRange.Parse("10.0.0.5") }, shared.ToArray());

        // a list of an IPv6 address and a mapped one inverts, each in its own family
        var inverted = new[] { IpRange.Parse("::1"), IpRange.Parse("::ffff:10.0.0.1") }.ToOrderedList().Invert();
        Assert.HasCount(4, inverted);
        Assert.IsFalse(inverted.Contains(IPAddress.Parse("10.0.0.1")));
        Assert.IsFalse(inverted.Contains(IPAddress.Parse("::1")));
        Assert.IsTrue(inverted.Contains(IPAddress.Parse("10.0.0.2")));
        Assert.IsTrue(inverted.Contains(IPAddress.Parse("::2")));
    }

    // The mapped addresses alone lie between the two ranges around them, and no lookup asks about those: the two
    // are one range. Kept apart, they would not be all of IPv6, and IpRange would take the gap between them, inverted,
    // for every IPv4 address.
    [TestMethod]
    public void The_two_ranges_around_the_mapped_addresses_are_one_range()
    {
        var around = new[] {
            IpRange.Parse(":: - ::fffe:ffff:ffff"),
            IpRange.Parse("::1:0:0:0 - ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")
        }.ToOrderedList();
        Assert.IsTrue(around.IsAllV6());
        Assert.IsTrue(around.Union(IpNetwork.AllV4.ToIpRange()).IsAll());

        var inverted = around.Invert(includeIPv4: false);
        Assert.IsFalse(inverted.Contains(IPAddress.Parse("8.8.8.8")));
        Assert.IsFalse(inverted.Contains(IPAddress.Parse("::ffff:8.8.8.8")));
        Assert.IsEmpty(inverted);

        // so are two ranges that end and start among the mapped addresses
        var inside = new[] { IpRange.Parse("::5 - ::ffff:1.2.3.4"), IpRange.Parse("::ffff:5.6.7.8 - 2001::1") }
            .ToOrderedList();
        CollectionAssert.AreEqual(new[] { IpRange.Parse("::5 - 2001::1") }, inside.ToArray());

        // but not a range that ends below them and one that starts after them: other addresses lie between
        var apart = new[] { IpRange.Parse(":: - ::5"), IpRange.Parse("::1:0:0:0 - 2001::1") }.ToOrderedList();
        Assert.HasCount(2, apart);
        Assert.IsFalse(apart.Contains(IPAddress.Parse("::7")));

        // what ::/80 and a range that starts at the first mapped address share is mapped addresses alone: left out
        var shared = new[] { IpNetwork.Parse("::/80").ToIpRange() }.ToOrderedList()
            .Intersect([IpRange.Parse("::ffff:0.0.0.0 - 2001::1")]);
        Assert.IsEmpty(shared);

        // nor is the mapped block a network of a range around it: it would read as 0.0.0.0/0
        CollectionAssert.AreEqual(
            new[] { IpNetwork.Parse("::fffe:ffff:ffff/128"), IpNetwork.Parse("::1:0:0:0/128") },
            IpNetwork.FromRange(IPAddress.Parse("::fffe:ffff:ffff"), IPAddress.Parse("::1:0:0:0")).ToArray());
    }

    // The bytes may be anyone's, or an older version's, which kept an IPv4-mapped range as it came.
    [TestMethod]
    public void Ranges_read_from_a_stream_are_sorted_and_merged()
    {
        using var stream = new MemoryStream(SerializeAsGiven(
            "10.0.0.90", "10.0.0.200", "::ffff:10.0.0.50", "::ffff:10.0.0.60", "10.0.0.0", "10.0.0.100",
            "9.0.0.0", "9.0.0.5"));

        var list = IpRangeOrderedList.Deserialize(stream);
        CollectionAssert.AreEqual(
            new[] { IpRange.Parse("9.0.0.0 - 9.0.0.5"), IpRange.Parse("10.0.0.0 - 10.0.0.200") }, list.ToArray());
        Assert.IsTrue(list.Contains(IPAddress.Parse("10.0.0.70")));
        Assert.IsTrue(list.Contains(IPAddress.Parse("9.0.0.3")));
    }

    // Serialize's form, with the ranges as they are given: each one's first address, then its last.
    private static byte[] SerializeAsGiven(params string[] addresses)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(addresses.Length / 2);
        foreach (var address in addresses) {
            var bytes = IPAddress.Parse(address).GetAddressBytes();
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }

        writer.Flush();
        return stream.ToArray();
    }

    [TestMethod]
    public void The_non_public_networks_hold_what_is_not_public()
    {
        var nonPublic = IpNetwork.NonPublicNetworks.ToIpRanges();
        string[] inside = [
            "0.0.0.0", "0.255.255.255", "10.1.2.3", "100.64.0.0", "100.127.255.255", "127.0.0.1", "127.0.0.53",
            "169.254.169.254", "172.16.0.1", "172.31.255.255", "192.0.0.8", "192.0.2.1", "192.168.1.1", "198.18.0.1",
            "198.19.255.255", "198.51.100.1", "203.0.113.1", "224.0.0.1", "239.255.255.250", "240.0.0.1",
            "255.255.255.255", "::", "::1", "64:ff9b::7f00:1", "100::1", "fc00::1", "fd00::1", "fe80::1", "ff02::1",
            "2001::1", "2001:0:ffff::1", "2001:2::1", "2001:2:0:ffff::1", "2001:10::1", "2001:1f:ffff::1",
            "2001:db8::1", "2002:7f00:1::1", "3fff::1", "3fff:fff::1", "4000::1", "8000::1",
            "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff"
        ];
        string[] outside = [
            "1.1.1.1", "8.8.8.8", "100.63.255.255", "100.128.0.0", "126.255.255.255", "128.0.0.1", "169.253.255.255",
            "172.15.255.255", "172.32.0.0", "192.0.1.1", "192.0.3.1", "198.17.255.255", "198.20.0.0", "198.51.101.1",
            "203.0.114.1", "223.255.255.255", "2000::1", "2001:200::1", "2001:4860:4860::8888", "2001:db7::1",
            "2001:db9::1", "2003::1", "2606:4700:4700::1111", "3ffe::1", "3fff:1000::1",
            // the parts of 2001::/23 the registry counts as reachable: the anycast addresses, AMT, AS112, ORCHIDv2
            "2001:1::1", "2001:1::2", "2001:2:1::1", "2001:3::1", "2001:4:112::1", "2001:20::1", "2001:1ff:ffff::1"
        ];

        foreach (var text in inside)
            Assert.IsTrue(nonPublic.Contains(IPAddress.Parse(text)), text);

        foreach (var text in outside)
            Assert.IsFalse(nonPublic.Contains(IPAddress.Parse(text)), text);

        // every list of local addresses is in it, and so is all of IPv6 outside the global unicast space
        var others = IpNetwork.LocalNetworks
            .Concat(IpNetwork.LoopbackNetworks)
            .Concat(IpNetwork.MulticastNetworks)
            .Concat(IpNetwork.AllGlobalUnicastV6.Invert())
            .ToIpRanges();
        Assert.IsEmpty(others.Exclude(nonPublic));
    }

    [TestMethod]
    public void IpRangeOrderedList_Contains_searches_mapped_and_mixed_ranges()
    {
        // an IPv4-mapped range is the IPv4 range it names, and merges with those it overlaps or touches; a range from
        // a mapped address to a plain IPv6 one is a range of IPv6 addresses, which holds no mapped address for a lookup
        var list = new[] {
            IpRange.Parse("10.0.0.0 - 10.0.0.255"),
            IpRange.Parse("::ffff:10.0.0.10 - ::ffff:10.0.0.20"),
            IpRange.Parse("10.0.1.0 - 10.0.1.255"),
            IpRange.Parse("::ffff:10.0.2.0 - ::ffff:10.0.2.255"),
            IpRange.Parse("::ffff:192.168.0.0 - 2001::1"),
            IpRange.Parse("2001:db8:: - 2001:db8::ffff")
        }.ToOrderedList();
        Assert.HasCount(3, list);
        Assert.AreEqual(IpRange.Parse("10.0.0.0 - 10.0.2.255"), list[0]);

        string[] inside = [
            "10.0.0.0", "10.0.0.15", "10.0.0.100", "10.0.0.255", "::ffff:10.0.0.100", "10.0.1.7", "10.0.2.1",
            "::ffff:10.0.2.1", "::1:0:0:0", "2001::1", "2001:db8::1", "2001:db8::ffff", "2001:db8::1%3"
        ];
        string[] outside = [
            "9.255.255.255", "::ffff:9.255.255.255", "10.0.3.0", "192.167.255.255", "192.168.0.1", "::ffff:192.168.0.1",
            "255.255.255.255", "::1", "2001::2", "2001:db8::1:0", "fe80::1"
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