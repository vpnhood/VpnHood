using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Test.Dom;
using ServerStatusModel = VpnHood.AccessServer.Models.ServerStatusModel;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class SyncTest
{
    [TestMethod]
    public async Task Sync_ServerStatuses()
    {
        var farm = await ServerFarmDom.Create(serverCount: 0);
        var serverDom = await farm.AddNewServer(false);
        var vhContext = farm.TestInit.VhContext;
        var vhReportContext = farm.TestInit.VhReportContext;

        var entity1 = await vhContext.ServerStatuses.AddAsync(new ServerStatusModel
        {
            ProjectId = farm.TestInit.ProjectId,
            ServerId = serverDom.ServerId,
            CreatedTime = DateTime.UtcNow,
            IsLast = true
        });
        var entity2 = await vhContext.ServerStatuses.AddAsync(new ServerStatusModel
        {
            ProjectId = farm.TestInit.ProjectId,
            ServerId = serverDom.ServerId,
            CreatedTime = DateTime.UtcNow,
            IsLast = false
        });
        var entity3 = await vhContext.ServerStatuses.AddAsync(new ServerStatusModel
        {
            ProjectId = farm.TestInit.ProjectId,
            ServerId = serverDom.ServerId,
            CreatedTime = DateTime.UtcNow,
            IsLast = false
        });

        await vhContext.SaveChangesAsync();

        await farm.TestInit.Sync(false); // do not flush

        var res = await vhContext.ServerStatuses.Where(x => x.ServerId == serverDom.ServerId).ToArrayAsync();
        Assert.AreEqual(1, res.Length);
        Assert.IsTrue(res[0].IsLast);

        // check report database
        Assert.IsTrue(
            await vhReportContext.ServerStatuses.AllAsync(x => x.ServerStatusId != entity1.Entity.ServerStatusId),
            "IsLast should not be copied");

        Assert.IsTrue(await vhReportContext.ServerStatuses.AnyAsync(x => x.ServerStatusId == entity2.Entity.ServerStatusId));
        Assert.IsTrue(await vhReportContext.ServerStatuses.AnyAsync(x => x.ServerStatusId == entity3.Entity.ServerStatusId));
    }

    [TestMethod]
    public async Task Sync_AccessUsages()
    {
        var farm = await ServerFarmDom.Create();
        var testInit = farm.TestInit;

        // init
        await testInit.Sync();
        Assert.IsFalse(testInit.VhContext.AccessUsages.Any(), "Sync should clear all access usages");

        // create token
        var accessTokenDom = await farm.CreateAccessToken();
        var sessionDom = await accessTokenDom.CreateSession();

        //-----------
        // check: add usage
        //-----------
        await sessionDom.AddUsage(10051, 20051);
        await sessionDom.AddUsage(20, 30);

        await testInit.FlushCache();
        var entities = await testInit.VhContext.AccessUsages.ToArrayAsync();
        Assert.IsTrue(entities.Length > 0);
        await testInit.Sync();
        Assert.IsFalse(testInit.VhContext.AccessUsages.Any(), "Sync should clear all access usages");

        //-----------
        // check: Copy to Report
        //-----------
        foreach (var entity in entities)
            Assert.IsTrue(await testInit.VhReportContext.AccessUsages.AnyAsync(x => x.AccessUsageId == entity.AccessUsageId));
    }

    [TestMethod]
    public async Task Sync_Sessions()
    {
        var farm = await ServerFarmDom.Create();
        var tokenDom = await farm.CreateAccessToken();

        farm.TestInit.AgentOptions.SessionPermanentlyTimeout = TimeSpan.FromSeconds(1);
        farm.TestInit.AgentOptions.SessionTemporaryTimeout = TimeSpan.FromSeconds(1);


        // create sessions
        var sessionDom1 = await tokenDom.CreateSession();
        var sessionDom2 = await tokenDom.CreateSession();
        var sessionDom3 = await tokenDom.CreateSession();
        var sessionDom4 = await tokenDom.CreateSession();
        await sessionDom1.CloseSession();
        await sessionDom2.CloseSession();
        await farm.TestInit.FlushCache();

        //-----------
        // check: Created Sessions
        //-----------
        var vhContext = farm.TestInit.VhContext;
        Assert.IsNotNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom1.SessionId)).EndTime);
        Assert.IsNotNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom2.SessionId)).EndTime);
        Assert.IsNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom3.SessionId)).EndTime);
        Assert.IsNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom4.SessionId)).EndTime);

        //-----------
        // check: Archived sessions must be cleared
        //-----------
        await Task.Delay(farm.TestInit.AgentOptions.SessionPermanentlyTimeout);
        await sessionDom3.AddUsage();
        await sessionDom4.AddUsage();
        await farm.TestInit.Sync();
        Assert.IsFalse(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom1.SessionId));
        Assert.IsFalse(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom2.SessionId));
        Assert.IsTrue(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom3.SessionId), "Should not remove open sessions");
        Assert.IsTrue(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom4.SessionId), "Should not remove open sessions");

        //-----------
        // check: Copy to Report
        //-----------
        await using var vhReportContext = farm.TestInit.VhReportContext;
        Assert.IsTrue(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom1.SessionId));
        Assert.IsTrue(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom2.SessionId));
        Assert.IsFalse(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom3.SessionId), "Should not remove open sessions");
        Assert.IsFalse(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom4.SessionId), "Should not remove open sessions");
    }
}