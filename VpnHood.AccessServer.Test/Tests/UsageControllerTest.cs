using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class UsageControllerTest : ControllerTest
{
    [TestMethod]
    public async Task GetUsageSummary()
    {
        var testInit2 = await TestInit.Create();

        var fillData = await testInit2.Fill();
        await testInit2.Sync();

        var res = await TestInit1.ProjectController.UsageSummaryAsync(testInit2.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.AreEqual(fillData.SessionRequests.Count, res.DeviceCount);
    }

    [TestMethod]
    public async Task LiveUsageSummary()
    {
        var testInit2 = await TestInit.Create();
        var serverController = testInit2.ServerController;

        // lost
        var server = await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        var agentController = testInit2.CreateAgentController(server.ServerId);
        testInit2.AppOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(2) / 3;
        await agentController.ConfigureAsync(await testInit2.NewServerInfo());
        await agentController.StatusAsync(new ServerStatus { SessionCount = 10 });
        await Task.Delay(2000);

        // active 2
        agentController = testInit2.CreateAgentController(testInit2.ServerId1);
        await agentController.StatusAsync(new ServerStatus { SessionCount = 1, TunnelReceiveSpeed = 100, TunnelSendSpeed = 50 });
        agentController = testInit2.CreateAgentController(testInit2.ServerId2);
        await agentController.StatusAsync(new ServerStatus { SessionCount = 2, TunnelReceiveSpeed = 300, TunnelSendSpeed = 200 });

        // notInstalled 4
        await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());

        // idle1
        server = await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        agentController = testInit2.CreateAgentController(server.ServerId);
        await agentController.ConfigureAsync(await testInit2.NewServerInfo());
        await agentController.StatusAsync(new ServerStatus { SessionCount = 0 });

        // idle2
        server = await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        agentController = testInit2.CreateAgentController(server.ServerId);
        await agentController.ConfigureAsync(await testInit2.NewServerInfo());
        await agentController.StatusAsync(new ServerStatus { SessionCount = 0 });

        // idle3
        server = await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        agentController = testInit2.CreateAgentController(server.ServerId);
        await agentController.ConfigureAsync(await testInit2.NewServerInfo());
        await agentController.StatusAsync(new ServerStatus { SessionCount = 0 });

        var liveUsageSummary = await testInit2.ProjectController.UsageLiveSummaryAsync(testInit2.ProjectId);
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
        var res = await TestInit1.ProjectController.UsageHistoryAsync(TestInit1.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.IsTrue(res.Count > 0);
    }
}