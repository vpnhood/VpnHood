using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.AccessServer.Test.Helper;
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

        // Order a new IP for the server
        var hostOrder = await farm.TestApp.HostOrdersClient.OrderNewIpAsync(farm.ProjectId, serverDom.ServerId);
        Assert.AreEqual(hostOrder.Status, HostOrderStatus.Pending);
        Assert.AreEqual(hostOrder.OrderType, HostOrderType.NewIp);
        Assert.IsTrue(hostOrder.CreatedTime >= startTime);
        Assert.IsNull(hostOrder.CompletedTime);
        Assert.IsNotNull(hostOrder.ProviderOrderId);
        Assert.IsNull(hostOrder.IpAddress);

        // get Order
        hostOrder = await farm.TestApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
        Assert.AreEqual(hostOrder.Status, HostOrderStatus.Pending);
        Assert.AreEqual(hostOrder.OrderType, HostOrderType.NewIp);
        Assert.IsTrue(hostOrder.CreatedTime >= startTime);
        Assert.IsNull(hostOrder.CompletedTime);
        Assert.IsNotNull(hostOrder.ProviderOrderId);
        Assert.IsNull(hostOrder.IpAddress);

        // finish order
        farm.TestApp.HostProvider.FinishOrders();

        // wait for the order to complete
        await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () =>
            (await farm.TestApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId)).Status);

        // check new op assigned to the server
        Assert.IsNotNull(hostOrder.CompletedTime);
        Assert.IsNotNull(hostOrder.IpAddress);
        var hostIp = (await farm.TestApp.HostOrdersClient.ListIpsAsync(farm.ProjectId)).Single(x => x.IpAddress.Equals(hostOrder.IpAddress));
        Assert.AreEqual(hostIp.HostProviderName, TestHostProvider.ProviderName);

        await serverDom.Reload();
        var accessPoint = serverDom.Server.AccessPoints.Single(x => x.IpAddress.Equals(hostIp.IpAddress));
        Assert.AreEqual(AccessPointMode.PublicInToken, accessPoint.AccessPointMode);
        Assert.IsTrue(accessPoint.IsListen);
    }
}