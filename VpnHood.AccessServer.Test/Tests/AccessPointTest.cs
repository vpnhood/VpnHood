using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessPointTest : BaseTest
{

    [TestMethod]
    public async Task Foo()
    {
        await Task.Delay(0);
    }

    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();
        var farm = await AccessPointGroupDom.Create(testInit, serverCount: 0);

        var accessPoint1 = await testInit.NewAccessPoint();
        var accessPoint2 = await testInit.NewAccessPoint();

        // create server
        var serverDom = await farm.AddNewServer(new ServerCreateParams
        {
            AccessPoints = new[]{ accessPoint1, accessPoint2 }
        });

        //-----------
        // check: accessPointGroupId is created
        //-----------
        var accessPoint1B = serverDom.Server.AccessPoints.ToArray()[0];
        var accessPoint2B = serverDom.Server.AccessPoints.ToArray()[1];
        Assert.AreEqual(accessPoint1.IpAddress, accessPoint1B.IpAddress);
        Assert.AreEqual(accessPoint1.TcpPort, accessPoint1B.TcpPort);
        Assert.AreEqual(accessPoint1.UdpPort, accessPoint1B.UdpPort);
        Assert.AreEqual(accessPoint1.AccessPointMode, accessPoint1B.AccessPointMode); // first group must be default
        Assert.AreEqual(accessPoint1.IsListen, accessPoint1B.IsListen); // first group must be default

        await serverDom.Reload();
        Assert.AreEqual(accessPoint1.IpAddress, accessPoint1B.IpAddress);
        Assert.AreEqual(accessPoint1.TcpPort, accessPoint1B.TcpPort);
        Assert.AreEqual(accessPoint1.UdpPort, accessPoint1B.UdpPort);
        Assert.AreEqual(accessPoint1.AccessPointMode, accessPoint1B.AccessPointMode); // first group must be default
        Assert.AreEqual(accessPoint1.IsListen, accessPoint1B.IsListen); // first group must be default

        Assert.AreEqual(accessPoint2.IpAddress, accessPoint2B.IpAddress);
        Assert.AreEqual(accessPoint2.TcpPort, accessPoint2B.TcpPort);
        Assert.AreEqual(accessPoint2.UdpPort, accessPoint2B.UdpPort);
        Assert.AreEqual(accessPoint2.AccessPointMode, accessPoint2B.AccessPointMode); // first group must be default
        Assert.AreEqual(accessPoint2.IsListen, accessPoint2B.IsListen); // first group must be default

        //-----------
        // check: update 
        //-----------
        var oldConfig = (await serverDom.UpdateStatus(serverDom.ServerInfo.Status)).ConfigCode;
        var accessPoint3 = await testInit.NewAccessPoint();
        await serverDom.Update(new ServerUpdateParams
        {
            AccessPoints = new PatchOfAccessPointOf { Value = new[] { accessPoint3 } }
        });
        var newConfig = (await serverDom.UpdateStatus(serverDom.ServerInfo.Status)).ConfigCode;
        Assert.AreNotEqual(oldConfig, newConfig);

        await serverDom.Reload();
        Assert.AreEqual(1, serverDom.Server.AccessPoints.ToArray().Length);
        var accessPoint3B = serverDom.Server.AccessPoints.ToArray()[0];
        Assert.AreEqual(accessPoint3.IpAddress, accessPoint3B.IpAddress);
        Assert.AreEqual(accessPoint3.TcpPort, accessPoint3B.TcpPort);
        Assert.AreEqual(accessPoint3.UdpPort, accessPoint3B.UdpPort);
        Assert.AreEqual(accessPoint3.AccessPointMode, accessPoint3B.AccessPointMode); // first group must be default
        Assert.AreEqual(accessPoint3.IsListen, accessPoint3B.IsListen); // first group must be default
    }

    [TestMethod]
    public async Task AccessPoints_should_not_change_if_AutoConfigure_is_off()
    {
        // create serverInfo
        var farm = await AccessPointGroupDom.Create();
        var accessPoints = farm.DefaultServer.Server.AccessPoints.ToArray();
        await farm.DefaultServer.Update(new ServerUpdateParams
        {
            AutoConfigure = new PatchOfBoolean { Value = false }
        });

        farm.DefaultServer.ServerInfo.PrivateIpAddresses = new[] { await TestInit1.NewIpV4(), await farm.TestInit.NewIpV4(), await TestInit1.NewIpV6() };
        farm.DefaultServer.ServerInfo.PublicIpAddresses = new[] { await TestInit1.NewIpV6(), await farm.TestInit.NewIpV4(), await TestInit1.NewIpV6() };

        // Configure
        await farm.DefaultServer.Configure();
        await farm.DefaultServer.Reload();
        Assert.AreEqual(accessPoints.Length, farm.DefaultServer.Server.AccessPoints.Count);
    }
}