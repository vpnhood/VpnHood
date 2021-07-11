using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using VpnHood.Client.Device;

namespace VpnHood.Test
{
    [TestClass]
    public class Test_IpNetwork
    {
        [TestMethod]
        public void Invert_Inify_Convert()
        {
            IpRange[] ipRangesSorted = new[] {
                IpRange.Parse("127.0.0.0 - 127.255.255.255"),
                IpRange.Parse("192.168.0.0 - 192.168.255.255"),
            };


            IpRange[] ipRanges = new[] { 
                IpRange.Parse("192.168.0.0 - 192.168.255.140"),
                IpRange.Parse("192.168.10.0 - 192.168.255.255"),
                IpRange.Parse("127.0.0.0 - 127.255.255.255"),
                IpRange.Parse("127.0.0.0 - 127.255.255.254"), //extra
            };

            var inverted = IpRange.Invert(ipRanges);
            IpRange[] expected = new[] {
                IpRange.Parse("0.0.0.0 - 126.255.255.255"),
                IpRange.Parse("128.0.0.0 - 192.167.255.255"),
                IpRange.Parse("192.169.0.0 - 255.255.255.255"),
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
            IpNetwork ipNetwork = IpNetwork.Parse("192.168.23.23/32");
            var inverted = ipNetwork.Invert();
            Assert.AreEqual(32, inverted.Length);
            CollectionAssert.AreEqual(new[] { ipNetwork }, IpNetwork.Invert(inverted));

            ipNetwork = IpNetwork.Parse("0.0.0.0/0");
            Assert.AreEqual(0, ipNetwork.Invert().Length);
            CollectionAssert.AreEqual(new[] { IpNetwork.Parse("0.0.0.0/0") }, IpNetwork.Invert(Array.Empty<IpNetwork>()));
        }

        [TestMethod]
        public void IpRange_IsInRange()
        {
            var ipRanges = new[] {
                IpRange.Parse("9.9.9.9 - 9.9.9.9"),
                IpRange.Parse("8.8.8.8"),
                IpRange.Parse("3.3.3.3-4.4.4.4"),
                IpRange.Parse("3.3.3.3-4.4.3.4"),
                IpRange.Parse("3.3.3.3-4.4.2.4"),
                IpRange.Parse("5.5.5.5-5.5.5.10"),
            };

            ipRanges = IpRange.Sort(ipRanges).ToArray();
            Assert.IsFalse(IpRange.IsInRange(ipRanges, IPAddress.Parse("9.9.9.7")));
            Assert.IsTrue(IpRange.IsInRange(ipRanges, IPAddress.Parse("8.8.8.8")));
            Assert.IsTrue(IpRange.IsInRange(ipRanges, IPAddress.Parse("9.9.9.9")));
            Assert.IsFalse(IpRange.IsInRange(ipRanges, IPAddress.Parse("4.4.4.5")));
            Assert.IsTrue(IpRange.IsInRange(ipRanges, IPAddress.Parse("4.4.4.3")));
        }

    }
}
