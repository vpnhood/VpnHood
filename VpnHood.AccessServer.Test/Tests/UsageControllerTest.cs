using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class UsageControllerTest : ControllerTest
{
    [TestMethod]
    public async Task GetUsageSummary()
    {
        var testInit2 = await TestInit.Create();

        var fillData = await testInit2.Fill();
        await TestInit1.SyncToReport();

        var projectController = new Apis.ProjectController(TestInit1.Http);
        var res = await projectController.UsageSummaryAsync(testInit2.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.AreEqual(fillData.SessionRequests.Count, res.DeviceCount);
    }

    [TestMethod]
    public async Task LiveUsageSummary()
    {
        var testInit2 = await TestInit.Create();

        var projectController = new Apis.ProjectController(testInit2.Http);
        var serverController = new Apis.ServerController(testInit2.Http);

        var agentController = testInit2.CreateAgentController2(testInit2.ServerId1);
        await agentController.StatusAsync(new Apis.ServerStatus { SessionCount = 1, TunnelReceiveSpeed = 100, TunnelSendSpeed = 50 });

        agentController = testInit2.CreateAgentController2(testInit2.ServerId2);
        await agentController.StatusAsync(new Apis.ServerStatus { SessionCount = 2, TunnelReceiveSpeed = 300, TunnelSendSpeed = 200 });

        // notInstalled 4
        await serverController.ServersPostAsync(testInit2.ProjectId, new Apis.ServerCreateParams());
        await serverController.ServersPostAsync(testInit2.ProjectId, new Apis.ServerCreateParams());
        await serverController.ServersPostAsync(testInit2.ProjectId, new Apis.ServerCreateParams());
        await serverController.ServersPostAsync(testInit2.ProjectId, new Apis.ServerCreateParams());

        // idle1
        var server = await serverController.ServersPostAsync(testInit2.ProjectId, new Apis.ServerCreateParams());
        agentController = testInit2.CreateAgentController2(server.ServerId);
        await agentController.ConfigureAsync(await testInit2.NewServerInfo2());
        await agentController.StatusAsync(new Apis.ServerStatus { SessionCount = 0 });

        // idle2
        server = await serverController.ServersPostAsync(testInit2.ProjectId, new Apis.ServerCreateParams());
        agentController = testInit2.CreateAgentController2(server.ServerId);
        await agentController.ConfigureAsync(await testInit2.NewServerInfo2());
        await agentController.StatusAsync(new Apis.ServerStatus { SessionCount = 0 });

        // idle3
        server = await serverController.ServersPostAsync(testInit2.ProjectId, new Apis.ServerCreateParams());
        agentController = testInit2.CreateAgentController2(server.ServerId);
        await agentController.ConfigureAsync(await testInit2.NewServerInfo2());
        await agentController.StatusAsync(new Apis.ServerStatus { SessionCount = 0 });

        // lost
        await using var vhScope = testInit2.WebApp.Services.CreateAsyncScope();
        await using var vhContext = vhScope.ServiceProvider.GetRequiredService<VhContext>();
        server = await serverController.ServersPostAsync(testInit2.ProjectId, new Apis.ServerCreateParams());
        agentController = testInit2.CreateAgentController2(server.ServerId);
        await agentController.ConfigureAsync(await testInit2.NewServerInfo2());
        await agentController.StatusAsync(new Apis.ServerStatus { SessionCount = 10 });
        var serverStatus = vhContext.ServerStatuses.First(x => x.ServerId == server.ServerId && x.IsLast);
        serverStatus.CreatedTime = DateTime.UtcNow - TimeSpan.FromMinutes(20);
        await vhContext.SaveChangesAsync();

        var liveUsageSummary = await projectController.UsageLiveSummaryAsync(testInit2.ProjectId);
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
        var projectController = TestInit1.CreateProjectController();
        var res = await projectController.GeUsageHistory(TestInit1.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.IsTrue(res.Length > 0);
    }
}