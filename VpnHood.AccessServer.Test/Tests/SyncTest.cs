using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Server;
using ServerStatusModel = VpnHood.AccessServer.Models.ServerStatusModel;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class SyncTest : BaseTest
{
    [TestMethod]
    public async Task Sync_ServerStatuses()
    {
        var serverClient = TestInit1.ServersClient;
        var server = await serverClient.CreateAsync(TestInit1.ProjectId, new ServerCreateParams());

        await using var vhScope = TestInit1.WebApp.Services.CreateAsyncScope();
        await using var vhContext = vhScope.ServiceProvider.GetRequiredService<VhContext>();

        var entity1 = await vhContext.ServerStatuses.AddAsync(new ServerStatusModel
        {
            ProjectId = TestInit1.ProjectId,
            ServerId = server.ServerId,
            CreatedTime = DateTime.UtcNow,
            IsLast = true
        });
        var entity2 = await vhContext.ServerStatuses.AddAsync(new ServerStatusModel
        {
            ProjectId = TestInit1.ProjectId,
            ServerId = server.ServerId,
            CreatedTime = DateTime.UtcNow,
            IsLast = false
        });
        var entity3 = await vhContext.ServerStatuses.AddAsync(new ServerStatusModel
        {
            ProjectId = TestInit1.ProjectId,
            ServerId = server.ServerId,
            CreatedTime = DateTime.UtcNow,
            IsLast = false
        });
        await vhContext.SaveChangesAsync();

        await TestInit1.Sync();

        var res = await vhContext.ServerStatuses.Where(x => x.ServerId == server.ServerId).ToArrayAsync();
        Assert.AreEqual(1, res.Length);
        Assert.IsTrue(res[0].IsLast);

        // check report database
        await using var vhReportContext = vhScope.ServiceProvider.GetRequiredService<VhReportContext>();
        Assert.IsTrue(
            await vhReportContext.ServerStatuses.AllAsync(x => x.ServerStatusId != entity1.Entity.ServerStatusId),
            "IsLast should not be copied");

        Assert.IsTrue(await vhReportContext.ServerStatuses.AnyAsync(x => x.ServerStatusId == entity2.Entity.ServerStatusId));
        Assert.IsTrue(await vhReportContext.ServerStatuses.AnyAsync(x => x.ServerStatusId == entity3.Entity.ServerStatusId));
    }

    [TestMethod]
    public async Task Sync_AccessUsages()
    {
        // init
        await using var vhScope = TestInit1.WebApp.Services.CreateAsyncScope();
        await using var vhContext = vhScope.ServiceProvider.GetRequiredService<VhContext>();
        await TestInit1.Sync();
        Assert.IsFalse(vhContext.AccessUsages.Any(), "Sync should clear all access usages");
        
        var agentClient = TestInit1.CreateAgentClient();

        // create token
        var accessToken = await TestInit1.AccessTokensClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1, IsPublic = false });
        var sessionRequestEx = TestInit1.CreateSessionRequestEx(accessToken);
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);

        //-----------
        // check: add usage
        //-----------
        await agentClient.Session_AddUsage(sessionResponseEx.SessionId,
            new UsageInfo { SentTraffic = 10051, ReceivedTraffic = 20051 });
        await agentClient.Session_AddUsage(sessionResponseEx.SessionId,
            new UsageInfo { SentTraffic = 20, ReceivedTraffic = 30 });

        await TestInit1.FlushCache();
        var entities = await vhContext.AccessUsages.ToArrayAsync();
        Assert.IsTrue(entities.Length > 0);
        await TestInit1.Sync();
        Assert.IsFalse(vhContext.AccessUsages.Any(), "Sync should clear all access usages");

        //-----------
        // check: Copy to Report
        //-----------
        await using var vhReportContext = vhScope.ServiceProvider.GetRequiredService<VhReportContext>();
        foreach (var entity in entities)
            Assert.IsTrue(await vhReportContext.AccessUsages.AnyAsync(x => x.AccessUsageId == entity.AccessUsageId));
    }

    [TestMethod]
    public async Task Sync_Sessions()
    {
        TestInit1.AgentOptions.SessionPermanentlyTimeout = TimeSpan.FromSeconds(1);
        TestInit1.AgentOptions.SessionTemporaryTimeout = TimeSpan.FromSeconds(1);
        var farmDom = await AccessPointGroupDom.Create();
        var tokenDom = await farmDom.CreateAccessToken(false);

        // create sessions
        var sessionDom1 = await tokenDom.CreateSession();
        var sessionDom2 = await tokenDom.CreateSession();
        var sessionDom3 = await tokenDom.CreateSession();
        var sessionDom4 = await tokenDom.CreateSession();
        await sessionDom1.CloseSession();
        await sessionDom2.CloseSession();
        await TestInit1.FlushCache();

        //-----------
        // check: Created Sessions
        //-----------
        var vhContext = farmDom.TestInit.VhContext;
        Assert.IsNotNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom1.SessionId)).EndTime);
        Assert.IsNotNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom2.SessionId)).EndTime);
        Assert.IsNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom3.SessionId)).EndTime);
        Assert.IsNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionDom4.SessionId)).EndTime);

        //-----------
        // check: Archived sessions must be cleared
        //-----------
        await Task.Delay(TestInit1.AgentOptions.SessionPermanentlyTimeout);
        await sessionDom3.AddUsage();
        await sessionDom4.AddUsage();
        await TestInit1.Sync();
        Assert.IsFalse(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom1.SessionId));
        Assert.IsFalse(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom2.SessionId));
        Assert.IsTrue(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom3.SessionId), "Should not remove open sessions");
        Assert.IsTrue(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionDom4.SessionId), "Should not remove open sessions");

        //-----------
        // check: Copy to Report
        //-----------
        await using var vhReportContext = farmDom.TestInit.VhReportContext;
        Assert.IsTrue(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom1.SessionId));
        Assert.IsTrue(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom2.SessionId));
        Assert.IsFalse(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom3.SessionId), "Should not remove open sessions");
        Assert.IsFalse(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionDom4.SessionId), "Should not remove open sessions");
    }
}