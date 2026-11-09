using System.Net;
using VpnHood.Net.Toolkit.Net;

// ReSharper disable StringLiteralTypo
namespace VpnHood.Test.Tests;

[TestClass]
public class IpNetworkTest : TestBase
{
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