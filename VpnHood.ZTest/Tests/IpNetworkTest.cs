using System;
using System.Linq;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client.Device;

// ReSharper disable StringLiteralTypo
namespace VpnHood.Test.Tests
{
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
            CollectionAssert.AreEqual(ipRangesSorted, IpRange.Sort(ipRanges));

            var inverted = IpRange.Invert(ipRanges);
            var expected = new[]
            {
                IpRange.Parse("0.0.0.0 - 126.255.255.255"),
                IpRange.Parse("128.0.0.0 - 192.167.255.255"),
                IpRange.Parse("192.169.0.0 - 255.255.255.255"),
                IpRange.Parse(":: - 99:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF"),
                IpRange.Parse("AA::01:0000 - FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF"),
            };

            CollectionAssert.AreEqual(expected, inverted);

            // check network
            CollectionAssert.AreEqual(IpNetwork.FromIpRange(expected), IpNetwork.Invert(IpNetwork.FromIpRange(ipRanges)));
            CollectionAssert.AreEqual(ipRangesSorted, IpNetwork.ToIpRange(IpNetwork.FromIpRange(ipRanges)));
        }

        [TestMethod]
        public void IpNetwork_Unit()
        {
            var ipNetwork = IpNetwork.Parse("192.168.23.23/32");
            var inverted = ipNetwork.Invert();
            Assert.AreEqual(32, inverted.Length);
            CollectionAssert.AreEqual(new[] { ipNetwork }, IpNetwork.Invert(inverted, true, false));

            ipNetwork = IpNetwork.AllV4;
            Assert.AreEqual(0, ipNetwork.Invert().Length);

            ipNetwork = IpNetwork.AllV6;
            Assert.AreEqual(0, ipNetwork.Invert().Length);

            CollectionAssert.AreEqual(new[] { IpNetwork.AllV4, IpNetwork.AllV6 }, IpNetwork.Invert(Array.Empty<IpNetwork>()));
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

 }
}