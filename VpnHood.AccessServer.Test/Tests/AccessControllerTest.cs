using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Common;
using AccessTokenCreateParams = VpnHood.AccessServer.DTOs.AccessTokenCreateParams;
using UsageInfo = VpnHood.Server.UsageInfo;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessControllerTest : ControllerTest
{
    [TestMethod]
    public async Task Get()
    {
        var agentController = TestInit1.CreateAgentController();
        var sessionRequestEx = TestInit1.CreateSessionRequestEx();
        var sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
        Assert.AreEqual(Common.Messaging.SessionErrorCode.Ok, sessionResponseEx.ErrorCode);

        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, 
            usageInfo: new UsageInfo {ReceivedTraffic = 10, SentTraffic = 20 });

        var accessControl = TestInit1.CreateAccessController();
        var accessDatas = await accessControl.GetUsages(TestInit1.ProjectId);
        var access = accessDatas.Single(x=>x.Access.AccessTokenId== sessionRequestEx.TokenId).Access;
        var accessData =  await accessControl.GetUsage(TestInit1.ProjectId, access.AccessId);
        Assert.AreEqual(30, accessData.Access.CycleTraffic);
    }

    [TestMethod]
    public async Task List()
    {
        var testInit2 = await TestInit.Create();

        var agentController = testInit2.CreateAgentController();
        var accessTokenControl = testInit2.CreateAccessTokenController();
        var actualAccessCount = 0;

        // ----------------
        // Create accessToken1 public
        // ----------------
        var accessToken1 = await accessTokenControl.Create(testInit2.ProjectId,
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
        var session = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);

        // accessToken1 - sessions2
        actualAccessCount++;
        sessionRequestEx = testInit2.CreateSessionRequestEx(accessToken1, hostEndPoint: testInit2.HostEndPointG2S1);
        session = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);

        // ----------------
        // Create accessToken2 public
        // ----------------
        var accessToken2 = await accessTokenControl.Create(testInit2.ProjectId,
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
        session = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);

        // accessToken2 - sessions2
        actualAccessCount++;
        sessionRequestEx = testInit2.CreateSessionRequestEx(accessToken2);
        session = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);

        // ----------------
        // Create accessToken3 private
        // ----------------
        var accessToken3 = await accessTokenControl.Create(testInit2.ProjectId,
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
        session = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);

        // accessToken3 - sessions2
        // actualAccessCount++; it is private!
        sessionRequestEx = testInit2.CreateSessionRequestEx(accessToken3);
        session = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);
        await agentController.Session_AddUsage(session.SessionId, closeSession: false, usageInfo: usageInfo);

        var accessController1 = testInit2.CreateAccessController();
        var res = await accessController1.GetUsages(testInit2.ProjectId);
            
        Assert.IsTrue(res.All(x=>x.Usage?.LastTime > dateTime));
        Assert.AreEqual(actualAccessCount, res.Length );
        Assert.AreEqual(actualAccessCount +1, res.Sum(x=>x.Usage?.DeviceCount) );
        Assert.AreEqual(actualAccessCount, res.Length);
        Assert.AreEqual(usageInfo.SentTraffic * actualAccessCount * 2 + 
                        usageInfo.SentTraffic * 2,  //private token shares its access
            res.Sum(x=>x.Usage?.SentTraffic));
        Assert.AreEqual(usageInfo.ReceivedTraffic * actualAccessCount * 2 + 
                        usageInfo.ReceivedTraffic * 2,  //private token shares its access
            res.Sum(x=>x.Usage?.ReceivedTraffic));

        // Check: Filter by Group
        res = await accessController1.GetUsages(testInit2.ProjectId, accessPointGroupId: testInit2.AccessPointGroupId2);
        Assert.AreEqual(2, res.Length);
        Assert.AreEqual(usageInfo.SentTraffic * 4, res.Sum(x => x.Usage?.SentTraffic));
        Assert.AreEqual(usageInfo.ReceivedTraffic * 4, res.Sum(x => x.Usage?.ReceivedTraffic));

        // range
        res = await accessController1.GetUsages(testInit2.ProjectId,recordIndex: 1, recordCount: 2);
        Assert.AreEqual(2, res.Length);
    }
}