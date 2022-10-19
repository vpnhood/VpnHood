using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class UsageClientTest : ClientTest
{
    [TestMethod]
    public async Task GetUsageSummary()
    {
        var testInit2 = await TestInit.Create();

        var fillData = await testInit2.Fill();
        await testInit2.Sync();

        var res = await TestInit1.ProjectClient.GetUsageSummaryAsync(testInit2.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.AreEqual(fillData.SessionRequests.Count, res.DeviceCount);
    }

    [TestMethod]
    public async Task LiveUsageSummary()
    {
        var testInit2 = await TestInit.Create();
        var serverClient = testInit2.ServerClient;

        // lost
        var server = await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams());
        var agentClient = testInit2.CreateAgentClient(server.ServerId);
        testInit2.AppOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(2) / 3;
        await agentClient.Server_Configure(await testInit2.NewServerInfo());
        await agentClient.Server_UpdateStatus(new ServerStatus { SessionCount = 10 });
        await Task.Delay(2000);

        // active 2
        agentClient = testInit2.CreateAgentClient(testInit2.ServerId1);
        await agentClient.Server_UpdateStatus(new ServerStatus { SessionCount = 1, TunnelReceiveSpeed = 100, TunnelSendSpeed = 50 });
        agentClient = testInit2.CreateAgentClient(testInit2.ServerId2);
        await agentClient.Server_UpdateStatus(new ServerStatus { SessionCount = 2, TunnelReceiveSpeed = 300, TunnelSendSpeed = 200 });

        // notInstalled 4
        await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams());
        await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams());
        await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams());
        await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams());

        // idle1
        server = await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams());
        agentClient = testInit2.CreateAgentClient(server.ServerId);
        await agentClient.Server_Configure(await testInit2.NewServerInfo());
        await agentClient.Server_UpdateStatus(new ServerStatus { SessionCount = 0 });

        // idle2
        server = await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams());
        agentClient = testInit2.CreateAgentClient(server.ServerId);
        await agentClient.Server_Configure(await testInit2.NewServerInfo());
        await agentClient.Server_UpdateStatus(new ServerStatus { SessionCount = 0 });

        // idle3
        server = await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams());
        agentClient = testInit2.CreateAgentClient(server.ServerId);
        await agentClient.Server_Configure(await testInit2.NewServerInfo());
        await agentClient.Server_UpdateStatus(new ServerStatus { SessionCount = 0 });

        var liveUsageSummary = await testInit2.ProjectClient.GeLiveUsageSummaryAsync(testInit2.ProjectId);
        Assert.AreEqual(10, liveUsageSummary.TotalServerCount);
        Assert.AreEqual(2, liveUsageSummary.ActiveServerCount);
        Assert.AreEqual(4, liveUsageSummary.NotInstalledServerCount);
        Assert.AreEqual(1, liveUsageSummary.LostServerCount);
        Assert.AreEqual(3, liveUsageSummary.IdleServerCount);
        Assert.AreEqual(250, liveUsageSummary.TunnelSendSpeed);
        Assert.AreEqual(400, liveUsageSummary.TunnelReceiveSpeed);
    }

    [TestMethod]
    public async Task GeUsageHistory()
    {
        var res = await TestInit1.ProjectClient.GeUsageHistoryAsync(TestInit1.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.IsTrue(res.Count > 0);
    }
}