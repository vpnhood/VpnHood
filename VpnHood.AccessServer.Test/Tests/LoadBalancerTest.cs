﻿using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Test.Dom;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class LoadBalancerTest
{
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
}
