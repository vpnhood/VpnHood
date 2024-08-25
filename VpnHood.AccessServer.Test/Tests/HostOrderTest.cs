using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class HostOrderTest
{
    [TestMethod]
    public async Task Order_new_ip()
    {
        var startTime = DateTime.UtcNow.AddSeconds(-1);
        using var farm = await ServerFarmDom.Create();
        var serverDom = farm.DefaultServer;

        var testHostProvider = await farm.TestApp.AddTestHostProvider();
        await serverDom.SetHostPanelDomain(testHostProvider.ProviderName);

        // Order a new IP for the server
        var hostOrder = await farm.TestApp.HostOrdersClient.CreateNewIpOrderAsync(farm.ProjectId,
            new HostOrderNewIp { ServerId = serverDom.ServerId });

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
        using var testApp = await TestApp.Create();
        using var farm = await ServerFarmDom.Create(testApp);
        var serverDom = farm.DefaultServer;

        // prepare host provider for a server
        var testHostProvider = await farm.TestApp.AddTestHostProvider();
        await serverDom.SetHostPanelDomain(testHostProvider.ProviderName);

        // Order a new IP for the server
        var hostOrder = await farm.TestApp.HostOrdersClient.CreateNewIpOrderAsync(farm.ProjectId,
            new HostOrderNewIp { ServerId = serverDom.ServerId });
        await testHostProvider.CompleteOrders();

        // make sure the order is completed
        await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () => {
            hostOrder = await farm.TestApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
            return hostOrder.Status;
        });

        // release the ip
        Assert.IsNotNull(hostOrder.NewIpOrderIpAddress);
        await farm.TestApp.HostOrdersClient.ReleaseIpAsync(farm.ProjectId,
            hostOrder.NewIpOrderIpAddress, ignoreProviderError: false);
        await testHostProvider.CompleteOrders();

        // the ip should not exist in list
        await VhTestUtil.AssertEqualsWait(false, async () => {
            var hostIps = await farm.TestApp.HostOrdersClient.ListIpsAsync(farm.ProjectId);
            return hostIps.Any(x => x.IpAddress == hostOrder.NewIpOrderIpAddress);
        });

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
        using var testApp = await TestApp.Create();
        using var farm = await ServerFarmDom.Create(testApp, serverCount: 0);
        var serverDom1 = await farm.AddNewServer();
        var serverDom2 = await farm.AddNewServer();

        // prepare host provider for a server
        var testHostProvider1 = await testApp.AddTestHostProvider();
        await serverDom1.SetHostPanelDomain(testHostProvider1.ProviderName);

        var testHostProvider2 = await testApp.AddTestHostProvider();
        await serverDom2.SetHostPanelDomain(testHostProvider2.ProviderName);

        // Order a new IP for the server
        var hostOrderS1 = new List<HostOrder>();
        var hostOrderS2 = new List<HostOrder>();
        for (var i = 0; i < 5; i++) {
            hostOrderS1.Add(await testApp.HostOrdersClient.CreateNewIpOrderAsync(farm.ProjectId,
                new HostOrderNewIp { ServerId = serverDom1.ServerId }));

            hostOrderS2.Add(await testApp.HostOrdersClient.CreateNewIpOrderAsync(farm.ProjectId,
                new HostOrderNewIp { ServerId = serverDom2.ServerId }));
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
        var hostIp1 = hostIps[0];
        var hostIp2 = hostIps[1];
        var hostIp3 = hostIps[9];
        await testApp.HostOrdersClient.ReleaseIpAsync(farm.ProjectId, hostIp1.IpAddress);
        await testApp.HostOrdersClient.ReleaseIpAsync(farm.ProjectId, hostIp2.IpAddress);
        await testApp.HostOrdersClient.ReleaseIpAsync(farm.ProjectId, hostIp3.IpAddress);

        // complete orders
        await testHostProvider1.CompleteOrders();
        await testHostProvider2.CompleteOrders();

        // ips should be removed
        await VhTestUtil.AssertEqualsWait(false, async () => {
            hostIps = (await testApp.HostOrdersClient.ListIpsAsync(farm.ProjectId)).ToArray();
            return hostIps.Any(x => x.IpAddress == hostIp1.IpAddress);
        });

        await VhTestUtil.AssertEqualsWait(false, async () => {
            hostIps = (await testApp.HostOrdersClient.ListIpsAsync(farm.ProjectId)).ToArray();
            return hostIps.Any(x => x.IpAddress == hostIp2.IpAddress);
        });

        await VhTestUtil.AssertEqualsWait(false, async () => {
            hostIps = (await testApp.HostOrdersClient.ListIpsAsync(farm.ProjectId)).ToArray();
            return hostIps.Any(x => x.IpAddress == hostIp3.IpAddress);
        });

        hostIps = (await testApp.HostOrdersClient.ListIpsAsync(farm.ProjectId)).ToArray();
        Assert.AreEqual(7, hostIps.Length);
        Assert.IsFalse(hostIps.Any(x => x.IpAddress == hostIp2.IpAddress));
        Assert.IsFalse(hostIps.Any(x => x.IpAddress == hostIp3.IpAddress));

        // the ips should not exist in server access points
        await serverDom1.Reload();
        Assert.IsFalse(serverDom1.Server.AccessPoints.Any(x => x.IpAddress == hostIp1.IpAddress));
        Assert.IsFalse(serverDom1.Server.AccessPoints.Any(x => x.IpAddress == hostIp2.IpAddress));
        Assert.IsFalse(serverDom1.Server.AccessPoints.Any(x => x.IpAddress == hostIp3.IpAddress));

        await serverDom2.Reload();
        Assert.IsFalse(serverDom2.Server.AccessPoints.Any(x => x.IpAddress == hostIp1.IpAddress));
        Assert.IsFalse(serverDom2.Server.AccessPoints.Any(x => x.IpAddress == hostIp2.IpAddress));
        Assert.IsFalse(serverDom2.Server.AccessPoints.Any(x => x.IpAddress == hostIp3.IpAddress));

        // the ips should not exist in server config
        await serverDom1.Configure();
        Assert.IsFalse(serverDom1.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == hostIp1.IpAddress));
        Assert.IsFalse(serverDom1.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == hostIp2.IpAddress));
        Assert.IsFalse(serverDom1.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == hostIp3.IpAddress));

        await serverDom2.Configure();
        Assert.IsFalse(serverDom2.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == hostIp1.IpAddress));
        Assert.IsFalse(serverDom2.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == hostIp2.IpAddress));
        Assert.IsFalse(serverDom2.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == hostIp3.IpAddress));
    }

    [TestMethod]
    public async Task Order_replace_ip()
    {
        using var testApp = await TestApp.Create(deleteOthers: true, aggressiveJob: true);
        using var farm = await ServerFarmDom.Create(testApp, serverCount: 0);
        var serverDom1 = await farm.AddNewServer();

        // prepare host provider for a server
        var testHostProvider = await testApp.AddTestHostProvider(autoCompleteDelay: TimeSpan.FromMilliseconds(300));
        await serverDom1.SetHostPanelDomain(testHostProvider.ProviderName);

        // Order a new IP for the server
        var hostOrders1 = new List<HostOrder>();
        for (var i = 0; i < 5; i++) {
            hostOrders1.Add(await testApp.HostOrdersClient.CreateNewIpOrderAsync(farm.ProjectId,
                new HostOrderNewIp { ServerId = serverDom1.ServerId }));
        }

        // wait for the orders to complete
        await VhTestUtil.AssertEqualsWait(true, async () => {
            hostOrders1 = (await testApp.HostOrdersClient.ListAsync(testApp.ProjectId)).ToList();
            return hostOrders1.All(x=>x.Status == HostOrderStatus.Completed);
        });

        // get all ips
        var hostIps = (await testApp.HostOrdersClient.ListIpsAsync(farm.ProjectId)).ToArray();
        Assert.AreEqual(5, hostIps.Length);

        // place replace order
        var oldIps = hostOrders1.Take(2).Select(x => x.NewIpOrderIpAddress).ToArray();
        Assert.IsTrue(oldIps.Any(x => x != null));
        testApp.Logger.LogInformation("Replacing old ips: {ips}", string.Join(", ", oldIps));

        var hostOrders2 = new List<HostOrder>();
        foreach (var oldIp in oldIps) {
            hostOrders2.Add(await testApp.HostOrdersClient.CreateNewIpOrderAsync(farm.ProjectId,
                new HostOrderNewIp {
                    ServerId = serverDom1.ServerId,
                    OldIpAddressReleaseTime = DateTime.UtcNow,
                    OldIpAddress = oldIp
                }));
        }

        // wait for the orders to complete
        foreach (var hostOrder in hostOrders2) {
            await VhTestUtil.AssertEqualsWait(HostOrderStatus.Completed, async () => {
                var hostOrderTemp = await testApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
                return hostOrderTemp.Status;
            });
        }

        // check servers has the ips
        await serverDom1.Reload();
        await serverDom1.Configure();
        foreach (var hostOrder in hostOrders2) {
            var order = await testApp.HostOrdersClient.GetAsync(farm.ProjectId, hostOrder.OrderId);
            Assert.IsTrue(serverDom1.Server.AccessPoints.Any(x => x.IpAddress == order.NewIpOrderIpAddress));
            Assert.IsTrue(serverDom1.ServerConfig.TcpEndPointsValue.Any(x => x.Address.ToString() == order.NewIpOrderIpAddress));
        }

        // old ips should be released
        foreach (var oldIp in oldIps) {
            await VhTestUtil.AssertEqualsWait<string?>(null, async () => {
                hostIps = (await testApp.HostOrdersClient.ListIpsAsync(farm.ProjectId)).ToArray();
                return hostIps.FirstOrDefault(x => x.IpAddress == oldIp)?.IpAddress;
            });
        }

        // the ips should not exist in server access points
        await serverDom1.Reload();
        Assert.IsFalse(serverDom1.Server.AccessPoints.Any(x => oldIps.Contains(x.IpAddress)));

        // the ips should not exist in server config
        await serverDom1.Configure();
        Assert.IsFalse(serverDom1.ServerConfig.TcpEndPointsValue.Any(x => oldIps.Contains(x.Address.ToString())));
    }
}
