using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Common;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessControllerTest : ControllerTest
{
    [TestMethod]
    public async Task Get()
    {
        var sessionRequestEx = TestInit1.CreateSessionRequestEx(TestInit1.AccessToken1);
        var sessionResponseEx = await TestInit1.AgentController1.CreateSessionAsync(sessionRequestEx);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);

        await TestInit1.AgentController1.AddSessionUsageAsync(sessionResponseEx.SessionId,
            new UsageInfo { ReceivedTraffic = 10, SentTraffic = 20 });
        await TestInit1.FlushCache();

        var accessDatas = await TestInit1.AccessController.GetUsagesAsync(TestInit1.ProjectId);
        var access = accessDatas.Single(x => x.Access.AccessTokenId == sessionRequestEx.TokenId).Access;
        var accessData = await TestInit1.AccessController.GetUsageAsync(TestInit1.ProjectId, access.AccessId);
        Assert.AreEqual(30, accessData.Access.CycleTraffic);
    }

    [TestMethod]
    public async Task List()
    {
        var testInit2 = await TestInit.Create();

        var agentController = testInit2.CreateAgentController();
        var accessTokenControl = new AccessTokenController(testInit2.Http);
        var actualAccessCount = 0;

        // ----------------
        // Create accessToken1 public
        // ----------------
        var accessToken1 = await accessTokenControl.CreateAsync(testInit2.ProjectId,
            new AccessTokenCreateParams
            {
                Secret = Util.GenerateSessionKey(),
                AccessTokenName = $"Access1_{Guid.NewGuid()}",
                AccessPointGroupId = testInit2.AccessPointGroupId2,
                IsPublic = true
            });


        var dateTime = DateTime.UtcNow.AddSeconds(-1);
        var usageInfo = new UsageInfo
        {
            ReceivedTraffic = 1000,
            SentTraffic = 500
        };
        await Task.Delay(100);

        // accessToken1 - sessions1
        actualAccessCount++;
        var sessionRequestEx = testInit2.CreateSessionRequestEx(accessToken1, hostEndPoint: testInit2.HostEndPointG2S1);
        var session = await agentController.CreateSessionAsync(sessionRequestEx);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);

        // accessToken1 - sessions2
        actualAccessCount++;
        sessionRequestEx = testInit2.CreateSessionRequestEx(accessToken1, hostEndPoint: testInit2.HostEndPointG2S1);
        session = await agentController.CreateSessionAsync(sessionRequestEx);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);

        // ----------------
        // Create accessToken2 public
        // ----------------
        var accessToken2 = await accessTokenControl.CreateAsync(testInit2.ProjectId,
            new AccessTokenCreateParams
            {
                Secret = Util.GenerateSessionKey(),
                AccessTokenName = $"Access2_{Guid.NewGuid()}",
                AccessPointGroupId = testInit2.AccessPointGroupId1,
                IsPublic = true
            });

        // accessToken2 - sessions1
        actualAccessCount++;
        sessionRequestEx = testInit2.CreateSessionRequestEx(accessToken2);
        session = await agentController.CreateSessionAsync(sessionRequestEx);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);

        // accessToken2 - sessions2
        actualAccessCount++;
        sessionRequestEx = testInit2.CreateSessionRequestEx(accessToken2);
        session = await agentController.CreateSessionAsync(sessionRequestEx);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);
        await testInit2.FlushCache();

        // ----------------
        // Create accessToken3 private
        // ----------------
        var accessToken3 = await accessTokenControl.CreateAsync(testInit2.ProjectId,
            new AccessTokenCreateParams
            {
                Secret = Util.GenerateSessionKey(),
                AccessTokenName = $"Access3_{Guid.NewGuid()}",
                AccessPointGroupId = testInit2.AccessPointGroupId1,
                IsPublic = false
            });

        // accessToken3 - sessions1
        actualAccessCount++;
        sessionRequestEx = testInit2.CreateSessionRequestEx(accessToken3);
        session = await agentController.CreateSessionAsync(sessionRequestEx);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);

        // accessToken3 - sessions2
        // actualAccessCount++; it is private!
        sessionRequestEx = testInit2.CreateSessionRequestEx(accessToken3);
        session = await agentController.CreateSessionAsync(sessionRequestEx);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);
        await agentController.AddSessionUsageAsync(session.SessionId, usageInfo);
        await testInit2.FlushCache();

        var accessController1 = new AccessController(testInit2.Http);
        var res = await accessController1.GetUsagesAsync(testInit2.ProjectId);

        Assert.IsTrue(res.All(x => x.Usage?.LastTime > dateTime));
        Assert.AreEqual(actualAccessCount, res.Count);
        Assert.AreEqual(actualAccessCount + 1, res.Sum(x => x.Usage?.DeviceCount));
        Assert.AreEqual(actualAccessCount, res.Count);
        Assert.AreEqual(usageInfo.SentTraffic * actualAccessCount * 2 + usageInfo.SentTraffic * 2,  //private token shares its access
            res.Sum(x => x.Usage?.SentTraffic));
        Assert.AreEqual(usageInfo.ReceivedTraffic * actualAccessCount * 2 + usageInfo.ReceivedTraffic * 2,  //private token shares its access
            res.Sum(x => x.Usage?.ReceivedTraffic));

        // Check: Filter by Group
        res = await accessController1.GetUsagesAsync(testInit2.ProjectId, accessPointGroupId: testInit2.AccessPointGroupId2);
        Assert.AreEqual(2, res.Count);
        Assert.AreEqual(usageInfo.SentTraffic * 4, res.Sum(x => x.Usage?.SentTraffic));
        Assert.AreEqual(usageInfo.ReceivedTraffic * 4, res.Sum(x => x.Usage?.ReceivedTraffic));

        // range
        res = await accessController1.GetUsagesAsync(testInit2.ProjectId, recordIndex: 1, recordCount: 2);
        Assert.AreEqual(2, res.Count);
    }
}