using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class LoadBalancerTest
{
    [TestMethod]
    public async Task AllowInAutoLocation_is_false()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        // Create and init servers
        var serverDom = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("10.0.0.1"));
        await serverDom.Update(new ServerUpdateParams { AllowInAutoLocation = new PatchOfBoolean { Value = false } });

        // fail if the location in auto
        var accessTokenDom = await farm.CreateAccessToken();
        var sessionDom = await accessTokenDom.CreateSession(autoRedirect: false, assertError: false);
        Assert.AreEqual(SessionErrorCode.AccessError, sessionDom.SessionResponseEx.ErrorCode);

        // fail if the location in set
        sessionDom = await accessTokenDom.CreateSession(autoRedirect: false, assertError: false, serverLocation: "10/*");
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);

    }

    [TestMethod]
    public async Task No_redirect_when_first_try_is_best()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        // Create and init servers
        await farm.AddNewServer();

        var accessTokenDom = await farm.CreateAccessToken();
        var sessionDom = await accessTokenDom.CreateSession(autoRedirect: false);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task By_availability()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        // Create and init servers
        var serverDom1 = await farm.AddNewServer();
        var serverDom2 = await farm.AddNewServer();
        var serverDom3 = await farm.AddNewServer(sendStatus: false);
        var serverDom4 = await farm.AddNewServer(configure: false);
        var serverDom5 = await farm.AddNewServer(configure: false, sendStatus: false);
        var serverDom6 = await farm.AddNewServer();

        // configure serverDom5 with ipv6
        serverDom5.ServerInfo.PublicIpAddresses = [await serverDom5.TestApp.NewIpV6(), await serverDom5.TestApp.NewIpV6()
        ];
        serverDom5.ServerInfo.PrivateIpAddresses = serverDom5.ServerInfo.PublicIpAddresses;
        await serverDom5.Configure();

        // make sure all accessPoints are initialized
        await farm.ReloadServers();

        // create access token
        var accessTokenDom = await farm.CreateAccessToken();

        // create sessions
        for (var i = 0; i < 10; i++)
        {
            var addressFamily = i == 9 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork; //only one IPv6 request
            var sessionDom = await accessTokenDom.CreateSession(addressFamily: addressFamily, autoRedirect: true);

            // find the server that create the session
            var serverDom = farm.FindServerByEndPoint(sessionDom.SessionRequestEx.HostEndPoint);
            serverDom.ServerInfo.Status.SessionCount++;
            await serverDom.SendStatus();
        }

        // some server should not be selected
        Assert.AreEqual(0, serverDom3.ServerStatus.SessionCount, "Should not use server in Configuring state.");
        Assert.AreEqual(0, serverDom4.ServerStatus.SessionCount, "Should not use server in Configuring state.");
        Assert.AreEqual(1, serverDom5.ServerStatus.SessionCount, "IpVersion was not respected.");

        // each server sessions must be 3
        Assert.AreEqual(3, serverDom1.ServerStatus.SessionCount);
        Assert.AreEqual(3, serverDom2.ServerStatus.SessionCount);
        Assert.AreEqual(3, serverDom6.ServerStatus.SessionCount);
    }

    [TestMethod]
    public async Task By_location_auto()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;
        farm.TestApp.AgentTestApp.AgentOptions.ServerUpdateStatusInterval = TimeSpan.FromHours(1);

        // Create and init servers
        var serverDom1 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("10.0.0.1"));
        var serverDom2 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("10.0.0.2"));
        var serverDom3 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("20.0.0.3"));
        var serverDom4 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("30.0.0.4"));
        var serverDom5 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("40.0.0.5"));

        // create access token
        var accessTokenDom = await farm.CreateAccessToken();

        // create sessions
        for (var i = 0; i < 10; i++)
        {
            var sessionDom = await accessTokenDom.CreateSession(autoRedirect: true);

            // find the server that create the session
            var serverDom = farm.FindServerByEndPoint(sessionDom.SessionRequestEx.HostEndPoint);
            serverDom.ServerInfo.Status.SessionCount++;
            await serverDom.SendStatus();
        }

        // distribute the sessions
        Assert.AreEqual(2, serverDom1.ServerStatus.SessionCount);
        Assert.AreEqual(2, serverDom2.ServerStatus.SessionCount);
        Assert.AreEqual(2, serverDom3.ServerStatus.SessionCount);
        Assert.AreEqual(2, serverDom4.ServerStatus.SessionCount);
        Assert.AreEqual(2, serverDom5.ServerStatus.SessionCount);
    }

    [TestMethod]
    public async Task By_location()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        // Create and init servers
        var serverDom1 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("10.0.0.1"));
        var serverDom2 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("10.0.0.2"));
        var serverDom3 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("20.0.0.3"));
        var serverDom4 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("30.0.0.4"));
        var serverDom5 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("40.0.0.5"));
        await farm.ReloadServers();

        // create access token
        var accessTokenDom = await farm.CreateAccessToken();

        // create sessions
        for (var i = 0; i < 6; i++)
        {
            var sessionDom = await accessTokenDom.CreateSession(autoRedirect: true, serverLocation: "10");
            Assert.AreEqual(sessionDom.SessionResponseEx.ServerLocation, "10/0");

            // find the server that create the session
            var serverDom = farm.FindServerByEndPoint(sessionDom.SessionRequestEx.HostEndPoint);
            serverDom.ServerInfo.Status.SessionCount++;
            await serverDom.SendStatus();
        }

        // distribute the sessions
        Assert.AreEqual(3, serverDom1.ServerStatus.SessionCount);
        Assert.AreEqual(3, serverDom2.ServerStatus.SessionCount);
        Assert.AreEqual(0, serverDom3.ServerStatus.SessionCount);
        Assert.AreEqual(0, serverDom4.ServerStatus.SessionCount);
        Assert.AreEqual(0, serverDom5.ServerStatus.SessionCount);
    }

    [TestMethod]
    public async Task By_server_power()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        // Create and init servers
        var serverDom1 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("10.0.0.1"), logicalCore: 4);
        var serverDom2 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("10.0.0.2"), logicalCore: 2);
        var serverDom3 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("20.0.0.3"), logicalCore: 2);
        var serverDom4 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("30.0.0.4"), logicalCore: 1);
        var serverDom5 = await farm.AddNewServer(gatewayIpV4: IPAddress.Parse("40.0.0.5"), logicalCore: 1);
        await farm.ReloadServers();

        // create access token
        var accessTokenDom = await farm.CreateAccessToken();

        // create sessions
        for (var i = 0; i < 10; i++)
        {
            var sessionDom = await accessTokenDom.CreateSession(autoRedirect: true);

            // find the server that create the session
            var serverDom = farm.FindServerByEndPoint(sessionDom.SessionRequestEx.HostEndPoint);
            serverDom.ServerInfo.Status.SessionCount++;
            await serverDom.SendStatus();
        }

        // distribute the sessions
        Assert.AreEqual(4, serverDom1.ServerStatus.SessionCount);
        Assert.AreEqual(2, serverDom2.ServerStatus.SessionCount);
        Assert.AreEqual(2, serverDom3.ServerStatus.SessionCount);
        Assert.AreEqual(1, serverDom4.ServerStatus.SessionCount);
        Assert.AreEqual(1, serverDom5.ServerStatus.SessionCount);
    }

    [TestMethod]
    public async Task Redirect_redirect_to_ipv4_when_server_has_no_ipv6()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;
        farm.TestApp.AgentTestApp.AgentOptions.LostServerThreshold = TimeSpan.FromDays(1);

        // Create and init servers
        var serverDom1 = await farm.AddNewServer(configure: false);
        serverDom1.ServerInfo = await farm.TestApp.NewServerInfo();
        serverDom1.ServerInfo.PrivateIpAddresses = [await farm.TestApp.NewIpV4(), await farm.TestApp.NewIpV6()];
        serverDom1.ServerInfo.PublicIpAddresses = [await farm.TestApp.NewIpV4(), await farm.TestApp.NewIpV6()];
        await serverDom1.Configure();

        var serverDom2 = await farm.AddNewServer(configure: false);
        serverDom2.ServerInfo = await farm.TestApp.NewServerInfo();
        serverDom2.ServerInfo.PrivateIpAddresses = [await farm.TestApp.NewIpV4()];
        serverDom2.ServerInfo.PublicIpAddresses = [await farm.TestApp.NewIpV4()];
        await serverDom2.Configure();

        var serverDom3 = await farm.AddNewServer(configure: false);
        serverDom3.ServerInfo = await farm.TestApp.NewServerInfo();
        serverDom3.ServerInfo.PrivateIpAddresses = [await farm.TestApp.NewIpV4()];
        serverDom3.ServerInfo.PublicIpAddresses = [await farm.TestApp.NewIpV4()];
        await serverDom3.Configure();

        var serverDom4 = await farm.AddNewServer(configure: false);
        serverDom4.ServerInfo = await farm.TestApp.NewServerInfo();
        serverDom4.ServerInfo.PrivateIpAddresses = [await farm.TestApp.NewIpV6()];
        serverDom4.ServerInfo.PublicIpAddresses = [await farm.TestApp.NewIpV6()];
        await serverDom4.Configure();


        await farm.ReloadServers();

        // create access token
        var accessTokenDom = await farm.CreateAccessToken();

        // create sessions for IpV6 clients
        for (var i = 0; i < 4; i++)
        {
            var sessionDom = await accessTokenDom.CreateSession(autoRedirect: true, addressFamily: AddressFamily.InterNetworkV6);

            // find the server that create the session
            var serverDom = farm.FindServerByEndPoint(sessionDom.SessionRequestEx.HostEndPoint);
            serverDom.ServerInfo.Status.SessionCount++;
            await serverDom.SendStatus();
        }

        Assert.AreEqual(1, serverDom1.ServerStatus.SessionCount);
        Assert.AreEqual(1, serverDom2.ServerStatus.SessionCount);
        Assert.AreEqual(1, serverDom3.ServerStatus.SessionCount);
        Assert.AreEqual(1, serverDom4.ServerStatus.SessionCount);
    }
}
