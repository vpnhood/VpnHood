using System;
using System.Linq;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client.Device;

namespace VpnHood.Test.Tests
{
    [TestClass]
    public class IpNetworkTest
    {
        [TestMethod]
        public void Invert_Unify_Convert()
        {
            //todo ip6 test

            var ipRangesSorted = new[]
            {
                IpRange.Parse("127.0.0.0 - 127.255.255.255"),
                IpRange.Parse("192.168.0.0 - 192.168.255.255")
            };


            var ipRanges = new[]
            {
                IpRange.Parse("192.168.0.0 - 192.168.255.140"),
                IpRange.Parse("192.168.10.0 - 192.168.255.255"),
                IpRange.Parse("127.0.0.0 - 127.255.255.255"),
                IpRange.Parse("127.0.0.0 - 127.255.255.254") //extra
            };

            var inverted = IpRange.Invert(ipRanges);
            var expected = new[]
            {
                IpRange.Parse("0.0.0.0 - 126.255.255.255"),
                IpRange.Parse("128.0.0.0 - 192.167.255.255"),
                IpRange.Parse("192.169.0.0 - 255.255.255.255")
            };

            CollectionAssert.AreEqual(expected, inverted);
            CollectionAssert.AreEqual(ipRangesSorted, IpRange.Sort(ipRanges));

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
            CollectionAssert.AreEqual(new[] { ipNetwork }, IpNetwork.Invert(inverted));

            //todo ip6 test
            ipNetwork = IpNetwork.Parse("0.0.0.0/0");
            Assert.AreEqual(0, ipNetwork.Invert().Length);
            CollectionAssert.AreEqual(new[] { IpNetwork.Parse("0.0.0.0/0") }, IpNetwork.Invert(Array.Empty<IpNetwork>()));
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
        public void Foo()
        {
            var ipNetwork = new IpNetwork(IPAddress.Parse("192.167.0.1"), 8);
            Console.WriteLine(ipNetwork);
            Console.WriteLine(ipNetwork.FirstIpAddress);
            Console.WriteLine(ipNetwork.LastIpAddress);

            var a1 = IpNetwork.FromIpRange(IPAddress.Parse("0.0.0.0"), IPAddress.Parse("255.255.255.255"));
        }
    }
}