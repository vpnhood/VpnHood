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
        await testHostProvider.FinishOrders(farm.TestApp);

        // wait for the order to complete
        await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () => {
            hostOrder = await farm.TestApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
            return hostOrder.Status;
        });

        // check new ip is assigned to the server
        Assert.IsNotNull(hostOrder.CompletedTime);
        Assert.IsNotNull(hostOrder.NewIpOrderIpAddress);
        var hostIp = (await farm.TestApp.HostOrdersClient.ListIpsAsync(farm.ProjectId)).Single(x => x.IpAddress.Equals(hostOrder.NewIpOrderIpAddress));
        Assert.AreEqual(hostIp.ProviderName, testHostProvider.ProviderName);

        await serverDom.Reload();
        var accessPoint = serverDom.Server.AccessPoints.Single(x => x.IpAddress.Equals(hostIp.IpAddress));
        Assert.AreEqual(AccessPointMode.PublicInToken, accessPoint.AccessPointMode);
        Assert.IsTrue(accessPoint.IsListen);
    }
}