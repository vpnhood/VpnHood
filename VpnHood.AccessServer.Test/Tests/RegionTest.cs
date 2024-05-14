﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Sockets;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class RegionTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testApp = await TestApp.Create();
        var regionClient = testApp.RegionsClient;

        //-----------
        // check: Create
        //-----------
        var createParams = new RegionCreateParams
        {
            RegionName = Guid.NewGuid().ToString(),
            CountryCode = "us"
        };
        await regionClient.CreateAsync(testApp.ProjectId, createParams);

        createParams = new RegionCreateParams
        {
            RegionName = Guid.NewGuid().ToString(),
            CountryCode = "gb"
        };
        var regionData = await regionClient.CreateAsync(testApp.ProjectId, createParams);

        //-----------
        // check: get
        //-----------
        regionData = await regionClient.GetAsync(testApp.ProjectId, regionData.Region.RegionId);
        Assert.AreEqual(createParams.CountryCode, regionData.Region.CountryCode);

        //-----------
        // check: update
        //-----------
        var updateParams = new RegionUpdateParams
        {
            RegionName = new PatchOfString{ Value = Guid.NewGuid().ToString() },
            CountryCode = new PatchOfString {Value = "UK" }
        };
        await regionClient.UpdateAsync(testApp.ProjectId, regionData.Region.RegionId, updateParams);
        regionData = await regionClient.GetAsync(testApp.ProjectId, regionData.Region.RegionId);
        Assert.AreEqual(updateParams.RegionName.Value, regionData.Region.RegionName);
        Assert.AreEqual(updateParams.CountryCode.Value, regionData.Region.CountryCode);

        //-----------
        // check: delete
        //-----------
        await regionClient.DeleteAsync(testApp.ProjectId, regionData.Region.RegionId);
        await VhTestUtil.AssertNotExistsException(regionClient.GetAsync(testApp.ProjectId, regionData.Region.RegionId));
    }
}

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
    public async Task By_region_auto()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        var region1 = (await farm.TestApp.AddRegion("us")).Region;
        var region2 = (await farm.TestApp.AddRegion("us")).Region;
        var region3 = (await farm.TestApp.AddRegion("gb")).Region;
        var region4 = (await farm.TestApp.AddRegion("fr")).Region;

        // Create and init servers
        var serverDom1 = await farm.AddNewServer(regionId: region1.RegionId);
        var serverDom2 = await farm.AddNewServer(regionId: region1.RegionId);
        var serverDom3 = await farm.AddNewServer(regionId: region2.RegionId);
        var serverDom4 = await farm.AddNewServer(regionId: region3.RegionId);
        var serverDom5 = await farm.AddNewServer(regionId: region4.RegionId);

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
    public async Task By_region_id()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        var region1 = (await farm.TestApp.AddRegion("us")).Region;
        var region2 = (await farm.TestApp.AddRegion("us")).Region;
        var region3 = (await farm.TestApp.AddRegion("gb")).Region;
        var region4 = (await farm.TestApp.AddRegion("fr")).Region;

        // Create and init servers
        var serverDom1 = await farm.AddNewServer(regionId: region1.RegionId);
        var serverDom2 = await farm.AddNewServer(regionId: region1.RegionId);
        var serverDom3 = await farm.AddNewServer(regionId: region2.RegionId);
        var serverDom4 = await farm.AddNewServer(regionId: region3.RegionId);
        var serverDom5 = await farm.AddNewServer(regionId: region4.RegionId);
        await farm.ReloadServers();

        // create access token
        var accessTokenDom = await farm.CreateAccessToken();

        // create sessions
        for (var i = 0; i < 6; i++)
        {
            var sessionDom = await accessTokenDom.CreateSession(autoRedirect: true, regionId: region1.RegionId.ToString());

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

        var region1 = (await farm.TestApp.AddRegion("us")).Region;
        var region2 = (await farm.TestApp.AddRegion("us")).Region;
        var region3 = (await farm.TestApp.AddRegion("gb")).Region;
        var region4 = (await farm.TestApp.AddRegion("fr")).Region;

        // Create and init servers
        var serverDom1 = await farm.AddNewServer(regionId: region1.RegionId, logicalCore: 4);
        var serverDom2 = await farm.AddNewServer(regionId: region1.RegionId, logicalCore: 2);
        var serverDom3 = await farm.AddNewServer(regionId: region2.RegionId, logicalCore: 2);
        var serverDom4 = await farm.AddNewServer(regionId: region3.RegionId, logicalCore: 1);
        var serverDom5 = await farm.AddNewServer(regionId: region4.RegionId, logicalCore: 1);
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
