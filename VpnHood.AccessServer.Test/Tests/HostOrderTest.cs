using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class HostOrderTest
{
    [TestMethod]
    public async Task Order_new_ip_for_a_server()
    {
        var startTime = DateTime.UtcNow;
        using var farm = await ServerFarmDom.Create();
        var serverDom = farm.DefaultServer;

        var testHostProvider = await farm.TestApp.AddTestHostProvider();
        testHostProvider.DefineIp(serverDom.ServerInfo.PublicIpAddresses);
        await serverDom.SetHostPanelDomain(testHostProvider.ProviderName);

        // Order a new IP for the server
        var hostOrder = await farm.TestApp.HostOrdersClient.OrderNewIpAsync(farm.ProjectId, serverDom.ServerId);
        Assert.AreEqual(hostOrder.Status, HostOrderStatus.Pending);
        Assert.AreEqual(hostOrder.OrderType, HostOrderType.NewIp);
        Assert.IsTrue(hostOrder.CreatedTime >= startTime);
        Assert.IsNull(hostOrder.CompletedTime);
        Assert.IsNotNull(hostOrder.ProviderOrderId);
        Assert.IsNull(hostOrder.NewIpOrderIpAddress);

        // get Order
        hostOrder = await farm.TestApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
        Assert.AreEqual(hostOrder.Status, HostOrderStatus.Pending);
        Assert.AreEqual(hostOrder.OrderType, HostOrderType.NewIp);
        Assert.IsTrue(hostOrder.CreatedTime >= startTime);
        Assert.IsNull(hostOrder.CompletedTime);
        Assert.IsNotNull(hostOrder.ProviderOrderId);
        Assert.IsNull(hostOrder.NewIpOrderIpAddress);

        // finish order
        await testHostProvider.CompleteOrders();

        // wait for the order to complete
        await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () => {
            hostOrder = await farm.TestApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
            return hostOrder.Status;
        });

        // check new ip is assigned to the server
        Assert.IsNotNull(hostOrder.CompletedTime);
        Assert.IsNotNull(hostOrder.NewIpOrderIpAddress);
        var orderedIp = IPAddress.Parse(hostOrder.NewIpOrderIpAddress);

        var hostIp = (await farm.TestApp.HostOrdersClient.ListIpsAsync(farm.ProjectId))
            .Single(x => x.IpAddress == orderedIp.ToString());
        Assert.AreEqual(hostIp.ProviderName, testHostProvider.ProviderName);

        await serverDom.Reload();
        var accessPoint = serverDom.Server.AccessPoints.Single(x => x.IpAddress == orderedIp.ToString());
        Assert.AreEqual(AccessPointMode.PublicInToken, accessPoint.AccessPointMode);
        Assert.IsTrue(accessPoint.IsListen);

        // server should get the new ip after reconfigure
        await serverDom.Configure();
        Assert.IsTrue(serverDom.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == orderedIp.ToString()));

    }

    [TestMethod]
    public async Task Order_release_ip()
    {
        using var farm = await ServerFarmDom.Create();
        var serverDom = farm.DefaultServer;

        // prepare host provider for a server
        var testHostProvider = await farm.TestApp.AddTestHostProvider();
        testHostProvider.DefineIp(serverDom.ServerInfo.PublicIpAddresses);
        await serverDom.SetHostPanelDomain(testHostProvider.ProviderName);

        // Order a new IP for the server
        var hostOrder = await farm.TestApp.HostOrdersClient.OrderNewIpAsync(farm.ProjectId, serverDom.ServerId);
        await testHostProvider.CompleteOrders();

        // make sure the order is completed
        await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () => {
            hostOrder = await farm.TestApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
            return hostOrder.Status;
        });

        // release the ip
        Assert.IsNotNull(hostOrder.NewIpOrderIpAddress);
        hostOrder = await farm.TestApp.HostOrdersClient.OrderReleaseIpAsync(farm.ProjectId,
            hostOrder.NewIpOrderIpAddress, ignoreProviderError: false);
        await testHostProvider.CompleteOrders();

        // make sure the release order has been completed
        // make sure the order is completed
        await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () => {
            hostOrder = await farm.TestApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
            return hostOrder.Status;
        });

        // the ip should not exist in list
        var hostIps = await farm.TestApp.HostOrdersClient.ListIpsAsync(farm.ProjectId);
        Assert.IsFalse(hostIps.Any(x => x.IpAddress == hostOrder.NewIpOrderIpAddress));

        // the ip should not exist in server access points
        await serverDom.Reload();
        Assert.IsFalse(serverDom.Server.AccessPoints.Any(x => x.IpAddress == hostOrder.NewIpOrderIpAddress));

        // the ip should not exist in server config
        await serverDom.Configure();
        Assert.IsFalse(
            serverDom.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == hostOrder.NewIpOrderIpAddress));

    }

    [TestMethod]
    public async Task Order_new_ip_and_release_ip()
    {
        var testApp = await TestApp.Create();
        using var farm = await ServerFarmDom.Create(testApp, serverCount: 0);
        var serverDom1 = await farm.AddNewServer();
        var serverDom2 = await farm.AddNewServer();

        // prepare host provider for a server
        var testHostProvider1 = await testApp.AddTestHostProvider();
        testHostProvider1.DefineIp(serverDom1.ServerInfo.PublicIpAddresses);
        await serverDom1.SetHostPanelDomain(testHostProvider1.ProviderName);

        var testHostProvider2 = await testApp.AddTestHostProvider();
        testHostProvider2.DefineIp(serverDom2.ServerInfo.PublicIpAddresses);
        await serverDom2.SetHostPanelDomain(testHostProvider2.ProviderName);

        // Order a new IP for the server
        var hostOrderS1 = new List<HostOrder>();
        var hostOrderS2 = new List<HostOrder>();
        for (var i = 0; i < 5; i++) {
            hostOrderS1.Add(await testApp.HostOrdersClient.OrderNewIpAsync(farm.ProjectId, serverDom1.ServerId));
            hostOrderS2.Add(await testApp.HostOrdersClient.OrderNewIpAsync(farm.ProjectId, serverDom2.ServerId));
        }

        // complete orders
        await testHostProvider1.CompleteOrders();
        await testHostProvider2.CompleteOrders();

        // wait for the orders to complete
        foreach (var hostOrder in hostOrderS1.Concat(hostOrderS2)) {
            await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () => {
                var hostOrderTemp = await testApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
                return hostOrderTemp.Status;
            });
        }

        // get all ips
        var hostIps = (await testApp.HostOrdersClient.ListIpsAsync(farm.ProjectId)).ToArray();
        Assert.AreEqual(10, hostIps.Length);

        // check servers has the ips
        await serverDom1.Reload();
        await serverDom1.Configure();
        foreach (var hostOrder in hostOrderS1) {
            var order = await testApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
            Assert.IsTrue(serverDom1.Server.AccessPoints.Any(x => x.IpAddress == order.NewIpOrderIpAddress));
            Assert.IsTrue(serverDom1.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == order.NewIpOrderIpAddress));
        }

        await serverDom2.Reload();
        await serverDom2.Configure();
        foreach (var hostOrder in hostOrderS2) {
            var order = await testApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
            Assert.IsTrue(serverDom2.Server.AccessPoints.Any(x => x.IpAddress == order.NewIpOrderIpAddress));
            Assert.IsTrue(serverDom2.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == order.NewIpOrderIpAddress));
        }


        // remove few ips
        var removeOrder1 = await testApp.HostOrdersClient.OrderReleaseIpAsync(farm.ProjectId, hostIps[0].IpAddress);
        var removeOrder2 = await testApp.HostOrdersClient.OrderReleaseIpAsync(farm.ProjectId, hostIps[1].IpAddress);
        var removeOrder3 = await testApp.HostOrdersClient.OrderReleaseIpAsync(farm.ProjectId, hostIps[9].IpAddress);

        // complete orders
        await testHostProvider1.CompleteOrders();
        await testHostProvider2.CompleteOrders();

        await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () => 
            (await testApp.HostOrdersClient.GetAsync(farm.ProjectId, removeOrder1.OrderId)).Status);
        await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () =>
            (await testApp.HostOrdersClient.GetAsync(farm.ProjectId, removeOrder2.OrderId)).Status);
        await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () =>
            (await testApp.HostOrdersClient.GetAsync(farm.ProjectId, removeOrder3.OrderId)).Status);

        
        // ips should be removed
        hostIps = (await testApp.HostOrdersClient.ListIpsAsync(farm.ProjectId)).ToArray();
        Assert.AreEqual(7, hostIps.Length);
        Assert.IsFalse(hostIps.Any(x => x.IpAddress == removeOrder1.NewIpOrderIpAddress));
        Assert.IsFalse(hostIps.Any(x => x.IpAddress == removeOrder2.NewIpOrderIpAddress));
        Assert.IsFalse(hostIps.Any(x => x.IpAddress == removeOrder3.NewIpOrderIpAddress));

        // the ips should not exist in server access points
        await serverDom1.Reload();
        Assert.IsFalse(serverDom1.Server.AccessPoints.Any(x => x.IpAddress == removeOrder1.NewIpOrderIpAddress));
        Assert.IsFalse(serverDom1.Server.AccessPoints.Any(x => x.IpAddress == removeOrder2.NewIpOrderIpAddress));
        Assert.IsFalse(serverDom1.Server.AccessPoints.Any(x => x.IpAddress == removeOrder3.NewIpOrderIpAddress));

        await serverDom2.Reload();
        Assert.IsFalse(serverDom2.Server.AccessPoints.Any(x => x.IpAddress == removeOrder1.NewIpOrderIpAddress));
        Assert.IsFalse(serverDom2.Server.AccessPoints.Any(x => x.IpAddress == removeOrder2.NewIpOrderIpAddress));
        Assert.IsFalse(serverDom2.Server.AccessPoints.Any(x => x.IpAddress == removeOrder3.NewIpOrderIpAddress));


        // the ips should not exist in server config
        await serverDom1.Configure();
        Assert.IsFalse(serverDom1.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == removeOrder1.NewIpOrderIpAddress));
        Assert.IsFalse(serverDom1.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == removeOrder2.NewIpOrderIpAddress));
        Assert.IsFalse(serverDom1.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == removeOrder3.NewIpOrderIpAddress));

        await serverDom2.Configure();
        Assert.IsFalse(serverDom2.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == removeOrder1.NewIpOrderIpAddress));
        Assert.IsFalse(serverDom2.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == removeOrder2.NewIpOrderIpAddress));
        Assert.IsFalse(serverDom2.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == removeOrder3.NewIpOrderIpAddress));


    }
}
