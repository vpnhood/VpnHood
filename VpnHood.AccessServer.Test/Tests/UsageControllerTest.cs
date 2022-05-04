using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
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
        await testInit2.Sync();

        var res = await TestInit1.ProjectController.UsageSummaryAsync(testInit2.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.AreEqual(fillData.SessionRequests.Count, res.DeviceCount);
    }

    [TestMethod]
    public async Task LiveUsageSummary()
    {
        var testInit2 = await TestInit.Create();

        var projectController = new ProjectController(testInit2.Http);
        var serverController = new ServerController(testInit2.Http);

        var agentController = testInit2.CreateAgentController(testInit2.ServerId1);
        await agentController.StatusAsync(new ServerStatus { SessionCount = 1, TunnelReceiveSpeed = 100, TunnelSendSpeed = 50 });

        agentController = testInit2.CreateAgentController(testInit2.ServerId2);
        await agentController.StatusAsync(new ServerStatus { SessionCount = 2, TunnelReceiveSpeed = 300, TunnelSendSpeed = 200 });

        // notInstalled 4
        await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());

        // idle1
        var server = await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
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

        // lost
        await using var vhScope = testInit2.WebApp.Services.CreateAsyncScope();
        await using var vhContext = vhScope.ServiceProvider.GetRequiredService<VhContext>();
        server = await serverController.ServersPostAsync(testInit2.ProjectId, new ServerCreateParams());
        agentController = testInit2.CreateAgentController(server.ServerId);
        await agentController.ConfigureAsync(await testInit2.NewServerInfo());
        await agentController.StatusAsync(new ServerStatus { SessionCount = 10 });
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
        var projectController = new ProjectController(TestInit1.Http);
        var res = await projectController.UsageHistoryAsync(TestInit1.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.IsTrue(res.Count > 0);
    }
}