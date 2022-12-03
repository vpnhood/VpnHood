using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Persistence;
using VpnHood.Server;
using ServerStatusModel = VpnHood.AccessServer.Models.ServerStatusModel;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class SyncTest : ClientTest
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
        Assert.IsTrue(
            await vhReportContext.ServerStatuses.AnyAsync(x => x.ServerStatusId == entity2.Entity.ServerStatusId));
        Assert.IsTrue(
            await vhReportContext.ServerStatuses.AnyAsync(x => x.ServerStatusId == entity3.Entity.ServerStatusId));
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
        // init
        var accessTokenClient = TestInit1.AccessTokensClient;
        var agentClient = TestInit1.CreateAgentClient();

        // create token
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId, new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1, IsPublic = false });

        // create sessions
        var sessionResponse1 = await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(accessToken, Guid.NewGuid()));
        var sessionResponse2 = await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(accessToken, Guid.NewGuid()));
        var sessionResponse3 = await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(accessToken, Guid.NewGuid()));
        var sessionResponse4 = await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(accessToken, Guid.NewGuid()));
        await agentClient.Session_Close(sessionResponse1.SessionId, new UsageInfo());
        await agentClient.Session_Close(sessionResponse2.SessionId, new UsageInfo());
        await TestInit1.FlushCache();

        //-----------
        // check: Created Sessions
        //-----------
        await using var vhScope = TestInit1.WebApp.Services.CreateAsyncScope();
        await using var vhContext = vhScope.ServiceProvider.GetRequiredService<VhContext>();
        Assert.IsNotNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionResponse1.SessionId)).EndTime);
        Assert.IsNotNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionResponse2.SessionId)).EndTime);
        Assert.IsNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionResponse3.SessionId)).EndTime);
        Assert.IsNull((await vhContext.Sessions.SingleAsync(x => x.SessionId == sessionResponse4.SessionId)).EndTime);
        
        await TestInit1.Sync();
        Assert.IsFalse(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionResponse1.SessionId));
        Assert.IsFalse(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionResponse2.SessionId));
        Assert.IsTrue(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionResponse3.SessionId), "Should not remove open sessions");
        Assert.IsTrue(await vhContext.Sessions.AnyAsync(x => x.SessionId == sessionResponse4.SessionId), "Should not remove open sessions");

        //-----------
        // check: Copy to Report
        //-----------
        await using var vhReportContext = vhScope.ServiceProvider.GetRequiredService<VhReportContext>();
        Assert.IsTrue(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionResponse1.SessionId));
        Assert.IsTrue(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionResponse2.SessionId));
        Assert.IsFalse(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionResponse3.SessionId), "Should not remove open sessions");
        Assert.IsFalse(await vhReportContext.Sessions.AnyAsync(x => x.SessionId == sessionResponse4.SessionId), "Should not remove open sessions");
    }
}