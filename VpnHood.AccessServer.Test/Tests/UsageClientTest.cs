using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Test.Sampler;
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
        var sampler = await SampleAccessPointGroup.Create(serverCount: 0);
        sampler.TestInit.AppOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(2) / 3;
        sampler.TestInit.AgentOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(2) / 3;

        // lost
        var sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 10 }) ;
        await Task.Delay(2000);

        // active 2
        sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 1, TunnelReceiveSpeed = 100, TunnelSendSpeed = 50 }) ;
        sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 2, TunnelReceiveSpeed = 300, TunnelSendSpeed = 200 });

        // notInstalled 4
        await sampler.AddNewServer(false);
        await sampler.AddNewServer(false);
        await sampler.AddNewServer(false);
        await sampler.AddNewServer(false);

        // idle1
        sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 0});

        // idle2
        sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 0 });

        // idle3
        sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 0 });

        var liveUsageSummary = await sampler.TestInit.ProjectClient.GetLiveUsageSummaryAsync(sampler.TestInit.ProjectId);
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