using System;
using System.Linq;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Common.Net;

// ReSharper disable StringLiteralTypo
namespace VpnHood.Test.Tests;

[TestClass]
public class IpNetworkTest
{
    [TestMethod]
    public void Invert_Unify_Convert()
    {
        var ipRangesSorted = new[]
        {
            IpRange.Parse("127.0.0.0 - 127.255.255.255"),
            IpRange.Parse("192.168.0.0 - 192.168.255.255"),
            IpRange.Parse("9A::0000 - AA::FFFF")
        };

        var ipRanges = new[]
        {
            IpRange.Parse("AA::0000 - AA::FCFC"),
            IpRange.Parse("192.168.0.0 - 192.168.255.140"),
            IpRange.Parse("9A::0000 - AA::AABB"),
            IpRange.Parse("AA::0000 - AA::FFF0"),
            IpRange.Parse("AA::FFF1 - AA::FFFF"),
            IpRange.Parse("192.168.10.0 - 192.168.255.255"),
            IpRange.Parse("127.0.0.0 - 127.255.255.255"),
            IpRange.Parse("127.0.0.0 - 127.255.255.254") //extra
        };
        CollectionAssert.AreEqual(ipRangesSorted, IpRange.Sort(ipRanges).ToArray());

        var inverted = IpRange.Invert(ipRanges);
        var expected = new[]
        {
            IpRange.Parse("0.0.0.0 - 126.255.255.255"),
            IpRange.Parse("128.0.0.0 - 192.167.255.255"),
            IpRange.Parse("192.169.0.0 - 255.255.255.255"),
            IpRange.Parse(":: - 99:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF"),
            IpRange.Parse("AA::01:0000 - FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF"),
        };

        CollectionAssert.AreEqual(expected, inverted.ToArray());

        // check network
        CollectionAssert.AreEqual(IpNetwork.FromIpRange(expected).ToArray(), IpNetwork.Invert(IpNetwork.FromIpRange(ipRanges)).ToArray());
        CollectionAssert.AreEqual(ipRangesSorted, IpRange.Sort(IpNetwork.ToIpRange(IpNetwork.FromIpRange(ipRanges))).ToArray());
    }

    [TestMethod]
    public void IpNetwork_Unit()
    {
        var ipNetwork = IpNetwork.Parse("192.168.23.23/32");
        var inverted = ipNetwork.Invert().ToArray();
        Assert.AreEqual(32, inverted.Length);
        CollectionAssert.AreEqual(new[] { ipNetwork }, IpNetwork.Invert(inverted, true, false).ToArray());

        ipNetwork = IpNetwork.AllV4;
        Assert.AreEqual(0, ipNetwork.Invert().ToArray().Length);

        ipNetwork = IpNetwork.AllV6;
        Assert.AreEqual(0, ipNetwork.Invert().ToArray().Length);

        CollectionAssert.AreEqual(new[] { IpNetwork.AllV4, IpNetwork.AllV6 },
            IpNetwork.Invert(Array.Empty<IpNetwork>()).ToArray());
    }

    [TestMethod]
    public void IpRange_IsInRange()
    {
        // simple
        var range = new IpRange(IPAddress.Parse("1.1.1.1"), IPAddress.Parse("1.1.1.10"));
        Assert.IsTrue(range.IsInRange(IPAddress.Parse("1.1.1.5")));
        Assert.IsFalse(range.IsInRange(IPAddress.Parse("1.1.1.12")));


        // array
        var ipRanges = new[]
        {
            IpRange.Parse("9.9.9.9 - 9.9.9.9"),
            IpRange.Parse("8.8.8.8"),
            IpRange.Parse("3.3.3.3 - 4.4.4.4"),
            IpRange.Parse("3.3.3.3 - 4.4.3.4"),
            IpRange.Parse("3.3.3.3 - 4.4.2.4"),
            IpRange.Parse("FF:: - FF::FF"),
            IpRange.Parse("EF:: - FF::FF"),
            IpRange.Parse("5.5.5.5-5.5.5.10")
        };

        ipRanges = IpRange.Sort(ipRanges).ToArray();
        Assert.IsFalse(IpRange.IsInRangeFast(ipRanges, IPAddress.Parse("9.9.9.7")));
        Assert.IsTrue(IpRange.IsInRangeFast(ipRanges, IPAddress.Parse("8.8.8.8")));
        Assert.IsTrue(IpRange.IsInRangeFast(ipRanges, IPAddress.Parse("9.9.9.9")));
        Assert.IsFalse(IpRange.IsInRangeFast(ipRanges, IPAddress.Parse("4.4.4.5")));
        Assert.IsTrue(IpRange.IsInRangeFast(ipRanges, IPAddress.Parse("4.4.4.3")));
        Assert.IsTrue(IpRange.IsInRangeFast(ipRanges, IPAddress.Parse("FF::F0")));
        Assert.IsFalse(IpRange.IsInRangeFast(ipRanges, IPAddress.Parse("AF::F0")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IpRange_Intersect(bool swap)
    {
        var ipRanges1 = new[]
        {
            IpRange.Parse("AA::FFF5 - AA::FFFF"),
            IpRange.Parse("192.168.10.0 - 192.168.12.12"),
            IpRange.Parse("190.190.10.0 - 190.190.11.0"), //ignore
            IpRange.Parse("30.30.10.50 - 30.30.10.100"),
            IpRange.Parse("20.20.10.50 - 20.20.10.55"),
            IpRange.Parse("20.20.10.60 - 20.20.10.100"),
        };

        var ipRanges2 = new[]
        {
            IpRange.Parse("AA::FFF1 - AA::FFF6"),
            IpRange.Parse("192.168.10.0 - 192.168.255.255"),
            IpRange.Parse("190.190.11.1 - 190.190.11.50"), //ignore
            IpRange.Parse("30.30.10.70 - 30.30.10.110"),
            IpRange.Parse("20.20.10.0 - 20.20.10.90"),
        };

        // Expected
        // AA::FFF5 - AA::FFF6

        var ranges = swap
            ? IpRange.Intersect(ipRanges2, ipRanges1).ToArray() 
            : IpRange.Intersect(ipRanges1, ipRanges2).ToArray();

        var i = 0;
        Assert.AreEqual("20.20.10.50-20.20.10.55", ranges[i++].ToString().ToUpper());
        Assert.AreEqual("20.20.10.60-20.20.10.90", ranges[i++].ToString().ToUpper());
        Assert.AreEqual("30.30.10.70-30.30.10.100", ranges[i++].ToString().ToUpper());
        Assert.AreEqual("192.168.10.0-192.168.12.12", ranges[i++].ToString().ToUpper());
        Assert.AreEqual("AA::FFF5-AA::FFF6", ranges[i++].ToString().ToUpper());
        Assert.AreEqual(i, ranges.Length);

    }
}