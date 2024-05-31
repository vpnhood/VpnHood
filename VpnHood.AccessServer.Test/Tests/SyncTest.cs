using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Test.Dom;
using ServerStatusModel = VpnHood.AccessServer.Persistence.Models.ServerStatusModel;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class SyncTest
{
    private static async Task<ServerStatusModel> AddServerStatus(VhContext vhContext, Guid projectId, Guid serverFarmId, Guid serverId, 
        bool isLast)
    {
        var entityEntry = await vhContext.ServerStatuses.AddAsync(new ServerStatusModel
        {
            ProjectId = projectId,
            ServerId = serverId,
            ServerFarmId = serverFarmId,
            CreatedTime = DateTime.UtcNow,
            AvailableMemory = 0,
            CpuUsage = 0,
            IsConfigure = true,
            IsLast = isLast,
            ServerStatusId = 0,
            SessionCount = 0,
            TcpConnectionCount = 0,
            UdpConnectionCount = 0,
            ThreadCount = 0,
            TunnelSendSpeed = 0,
            TunnelReceiveSpeed = 0
        });

        return entityEntry.Entity;
    }

    [TestMethod]
    public async Task Sync_ServerStatuses()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        var serverDom = await farm.AddNewServer(configure: false);
        var vhContext = farm.TestApp.VhContext;
        var vhReportContext = farm.TestApp.VhReportContext;

        var entity1 = await AddServerStatus(vhContext, farm.TestApp.ProjectId, farm.ServerFarmId, serverDom.ServerId, true);
        var entity2 = await AddServerStatus(vhContext, farm.TestApp.ProjectId, farm.ServerFarmId, serverDom.ServerId, false);
        var entity3 = await AddServerStatus(vhContext, farm.TestApp.ProjectId, farm.ServerFarmId, serverDom.ServerId, false);
        await vhContext.SaveChangesAsync();

        await farm.TestApp.Sync(false); // do not flush

        var res = await vhContext.ServerStatuses.Where(x => x.ServerId == serverDom.ServerId).ToArrayAsync();
        Assert.AreEqual(1, res.Length);
        Assert.IsTrue(res[0].IsLast);

        // check report database
        Assert.IsTrue(
            await vhReportContext.ServerStatuses.AllAsync(x => x.ServerStatusId != entity1.ServerStatusId),
            "IsLast should not be copied");

        Assert.IsTrue(await vhReportContext.ServerStatuses.AnyAsync(x => x.ServerStatusId == entity2.ServerStatusId));
        Assert.IsTrue(await vhReportContext.ServerStatuses.AnyAsync(x => x.ServerStatusId == entity3.ServerStatusId));
    }

    [TestMethod]
    public async Task Sync_AccessUsages()
    {
        using var farm = await ServerFarmDom.Create();
        var testApp = farm.TestApp;

        // init
        await testApp.Sync();
        Assert.IsFalse(testApp.VhContext.AccessUsages.Any(), "Sync should clear all access usages");

        // create token
        var accessTokenDom = await farm.CreateAccessToken();
        var sessionDom = await accessTokenDom.CreateSession();

        //-----------
        // check: add usage
        //-----------
        await sessionDom.AddUsage(10051, 20051);
        await sessionDom.AddUsage(20, 30);

        await testApp.FlushCache();
        var entities = await testApp.VhContext.AccessUsages.ToArrayAsync();
        Assert.IsTrue(entities.Length > 0);
        await testApp.Sync();
        Assert.IsFalse(testApp.VhContext.AccessUsages.Any(), "Sync should clear all access usages");

        //-----------
        // check: Copy to Report
        //-----------
        foreach (var entity in entities)
            Assert.IsTrue(await testApp.VhReportContext.AccessUsages.AnyAsync(x => x.AccessUsageId == entity.AccessUsageId));
    }

    [TestMethod]
    public async Task Sync_Sessions()
    {
        using var farm = await ServerFarmDom.Create();
        var tokenDom = await farm.CreateAccessToken();

        farm.TestApp.AgentTestApp.AgentOptions.SessionPermanentlyTimeout = TimeSpan.FromSeconds(1);
        farm.TestApp.AgentTestApp.AgentOptions.SessionTemporaryTimeout = TimeSpan.FromSeconds(1);


        // create sessions
        var sessionDom1 = await tokenDom.CreateSession();
        var sessionDom2 = await tokenDom.CreateSession();
        var sessionDom3 = await tokenDom.CreateSession();
        var sessionDom4 = await tokenDom.CreateSession();
        await sessionDom1.CloseSession();
        await sessionDom2.CloseSession();
        await farm.TestApp.FlushCache();

        //-----------
        // check: Created Sessions
        //-----------
        var vhContext = farm.TestApp.VhContext;
        Assert.IsNotNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom1.SessionId)).EndTime);
        Assert.IsNotNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom2.SessionId)).EndTime);
        Assert.IsNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom3.SessionId)).EndTime);
        Assert.IsNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom4.SessionId)).EndTime);

        //-----------
        // check: Archived sessions must be cleared
        //-----------
        await Task.Delay(farm.TestApp.AgentTestApp.AgentOptions.SessionPermanentlyTimeout);
        await sessionDom3.AddUsage();
        await sessionDom4.AddUsage();
        await farm.TestApp.Sync();
        Assert.IsFalse(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom1.SessionId));
        Assert.IsFalse(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom2.SessionId));
        Assert.IsTrue(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom3.SessionId), "Should not remove open sessions");
        Assert.IsTrue(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom4.SessionId), "Should not remove open sessions");

        //-----------
        // check: Copy to Report
        //-----------
        await using var vhReportContext = farm.TestApp.VhReportContext;
        Assert.IsTrue(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom1.SessionId));
        Assert.IsTrue(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom2.SessionId));
        Assert.IsFalse(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom3.SessionId), "Should not remove open sessions");
        Assert.IsFalse(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom4.SessionId), "Should not remove open sessions");
    }

}

