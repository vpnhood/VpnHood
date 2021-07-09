using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using VpnHood.Client.Device;

namespace VpnHood.Test
{
    [TestClass]
    public class Test_IpNetwork
    {
        [TestMethod]
        public void Invert_Inify_Convert()
        {
            IpRange[] ipRangesU = new[] {
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
            CollectionAssert.AreEqual(ipRangesU, IpRange.Unify(ipRanges));

            // check network
            CollectionAssert.AreEqual(IpNetwork.FromIpRange(expected), IpNetwork.Invert(IpNetwork.FromIpRange(ipRanges)));
            CollectionAssert.AreEqual(ipRangesU, IpNetwork.ToIpRange(IpNetwork.FromIpRange(ipRanges)));
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
    }
}
