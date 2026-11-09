using System.Net;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

[TestClass]
public class IpEndPointValueTest : TestBase
{
    [TestMethod]
    public void Address_members_keep_the_address_and_its_scope()
    {
        var address = IPAddress.Parse("fe80::1%3");
        var endPoint = new IpEndPointValue(address, 443);
        Assert.AreEqual(address, endPoint.Address);
        Assert.AreEqual(3L, endPoint.Address.ScopeId);
        Assert.AreEqual(address.ToValue(), endPoint.AddressValue);
        Assert.AreEqual(443, endPoint.Port);
        Assert.AreEqual(new IPEndPoint(address, 443), endPoint.ToIPEndPoint());
        Assert.AreEqual(endPoint, new IPEndPoint(address, 443).ToValue());

        var (deconstructedAddress, deconstructedPort) = endPoint;
        Assert.AreEqual(address, deconstructedAddress);
        Assert.AreEqual(443, deconstructedPort);

        var moved = endPoint with { Address = IPAddress.Parse("10.0.0.1") };
        Assert.AreEqual(IPAddress.Parse("10.0.0.1"), moved.Address);
        Assert.AreEqual(443, moved.Port);
        Assert.IsTrue(moved.IsV4);
        Assert.AreEqual(new IpEndPointValue(address, 80), endPoint with { Port = 80 });
    }

    [TestMethod]
    public void Equals_by_address_and_port()
    {
        var endPoint = new IpEndPointValue(IPAddress.Parse("10.0.0.2"), 80);
        var same = new IpEndPointValue(IPAddress.Parse("10.0.0.2").ToValue(), 80);
        Assert.IsTrue(endPoint == same);
        Assert.AreEqual(endPoint.GetHashCode(), same.GetHashCode());
        Assert.AreNotEqual(endPoint, new IpEndPointValue(IPAddress.Parse("10.0.0.2"), 81));
        Assert.AreNotEqual(endPoint, new IpEndPointValue(IPAddress.Parse("::ffff:10.0.0.2"), 80));
        Assert.AreNotEqual(
            new IpEndPointValue(IPAddress.Parse("fe80::1"), 80),
            new IpEndPointValue(IPAddress.Parse("fe80::1%3"), 80));
    }

    [TestMethod]
    public void Prints_its_address_and_port()
    {
        Assert.AreEqual("IpEndPointValue { Address = 10.0.0.2, Port = 80 }",
            new IpEndPointValue(IPAddress.Parse("10.0.0.2"), 80).ToString());
        Assert.AreEqual("IpEndPointValue { Address = fe80::1%3, Port = 443 }",
            new IpEndPointValue(IPAddress.Parse("fe80::1%3"), 443).ToString());
        Assert.AreEqual("IpEndPointValue { Address = , Port = 0 }", default(IpEndPointValue).ToString());
    }

    [TestMethod]
    public void Default_holds_no_address()
    {
        var endPoint = default(IpEndPointValue);
        Assert.AreEqual(default, endPoint.AddressValue);
        Assert.IsFalse(endPoint.IsV4);
        Assert.IsFalse(endPoint.IsV6);
        Assert.ThrowsExactly<InvalidOperationException>(() => endPoint.Address);
        Assert.ThrowsExactly<InvalidOperationException>(() => endPoint.ToIPEndPoint());
    }
}
