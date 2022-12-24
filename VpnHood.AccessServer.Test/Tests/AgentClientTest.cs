using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Services;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AgentClientSessionTest : ClientTest
{
    [TestMethod]
    public async Task Session_Create_Status_TrafficOverflow()
    {
        var accessTokenClient = TestInit1.AccessTokensClient;

        // create accessTokenModel
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                MaxTraffic = 14
            });

        // get access
        var agentClient = TestInit1.CreateAgentClient();
        var sessionResponseEx = await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(accessToken));

        //-----------
        // check: add usage
        //-----------
        var sessionResponse = await agentClient.Session_AddUsage(sessionResponseEx.SessionId,
            new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
        await TestInit1.FlushCache();
        Assert.AreEqual(5, sessionResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, sessionResponse.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.AccessTrafficOverflow, sessionResponse.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_Status_No_TrafficOverflow_when_maxTraffic_is_zero()
    {
        var accessTokenClient = TestInit1.AccessTokensClient;

        // create accessTokenModel
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                MaxTraffic = 0
            });
        var agentClient = TestInit1.CreateAgentClient();

        //-----------
        // check: add usage
        //-----------
        var sessionResponseEx =
            await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(accessToken));
        var sessionResponse = await agentClient.Session_AddUsage(sessionResponseEx.SessionId, new UsageInfo
        {
            SentTraffic = 5,
            ReceivedTraffic = 10
        });
        Assert.AreEqual(5, sessionResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, sessionResponse.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponse.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_success()
    {
        // create token
        var accessTokenClient = TestInit1.AccessTokensClient;
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                MaxTraffic = 100,
                ExpirationTime = new DateTime(2040, 1, 1),
                Lifetime = 0,
                MaxDevice = 22
            });

        var beforeUpdateTime = DateTime.UtcNow.AddSeconds(-1);
        var sessionRequestEx = TestInit1.CreateSessionRequestEx(accessToken,
            hostEndPoint: TestInit1.HostEndPointG1S1, clientIp: TestInit1.ClientIp1);
        sessionRequestEx.ClientInfo.UserAgent = "userAgent1";
        var clientInfo = sessionRequestEx.ClientInfo;

        var agentClient = TestInit1.CreateAgentClient();
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        var accessTokenData = await accessTokenClient.GetAsync(TestInit1.ProjectId, sessionRequestEx.TokenId);

        Assert.IsNotNull(sessionResponseEx.AccessUsage);
        Assert.IsTrue(sessionResponseEx.SessionId > 0);
        Assert.AreEqual(new DateTime(2040, 1, 1), sessionResponseEx.AccessUsage!.ExpirationTime);
        Assert.AreEqual(22, sessionResponseEx.AccessUsage.MaxClientCount);
        Assert.AreEqual(100, sessionResponseEx.AccessUsage.MaxTraffic);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.ReceivedTraffic);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.SentTraffic);
        Assert.IsNotNull(sessionResponseEx.SessionKey);
        Assert.IsTrue(accessTokenData.Access!.CreatedTime >= beforeUpdateTime);
        Assert.IsTrue(accessTokenData.Access!.CreatedTime >= beforeUpdateTime);

        // check Device id and its properties are created 
        var deviceClient = TestInit1.DevicesClient;
        var device = await deviceClient.FindByClientIdAsync(TestInit1.ProjectId, clientInfo.ClientId);
        Assert.AreEqual(clientInfo.ClientId, device.ClientId);
        Assert.AreEqual(clientInfo.UserAgent, device.UserAgent);
        Assert.AreEqual(clientInfo.ClientVersion, device.ClientVersion);

        // check updating same client
        sessionRequestEx.ClientIp = TestInit1.ClientIp2;
        sessionRequestEx.ClientInfo.UserAgent = "userAgent2";
        sessionRequestEx.ClientInfo.ClientVersion = "200.0.0";
        await agentClient.Session_Create(sessionRequestEx);
        device = await deviceClient.FindByClientIdAsync(TestInit1.ProjectId, sessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(sessionRequestEx.ClientInfo.UserAgent, device.UserAgent);
        Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, device.ClientVersion);
    }

    private async Task<Models.AccessModel> GetAccessFromSession(long sessionId)
    {
        await using var scope = TestInit1.WebApp.Services.CreateAsyncScope();
        await using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        var session = await vhContext.Sessions
            .Include(x => x.Access)
            .SingleAsync(x => x.SessionId == sessionId);
        return session.Access!;
    }

    [TestMethod]
    public async Task Session_Get()
    {
        var accessPointGroupDom = await AccessPointGroupDom.Create();
        var accessTokenDom = await accessPointGroupDom.CreateAccessToken(false);

        var sessionDom = await accessTokenDom.CreateSession();
        Assert.IsNotNull(sessionDom.SessionResponseEx.SessionKey);
        
        // GetSession
        var oldSessionResponseEx = sessionDom.SessionResponseEx;
        await sessionDom.Reload();
        CollectionAssert.AreEqual(oldSessionResponseEx.SessionKey, sessionDom.SessionResponseEx.SessionKey);

        //check SessionKey
        await sessionDom.Reload();
    }

    [TestMethod]
    public async Task Session_Get_should_update_session_lastUsedTime()
    {
        var accessPointGroupDom = await AccessPointGroupDom.Create();
        var accessTokenDom = await accessPointGroupDom.CreateAccessToken(false);

        var sessionDom = await accessTokenDom.CreateSession();
        var session = await sessionDom.GetSessionFromCache();
        Assert.IsTrue(session.LastUsedTime >= accessPointGroupDom.CreatedTime);

        // get the token again
        var time = DateTime.UtcNow;
        await Task.Delay(1000);
        await sessionDom.Reload(); //session get
        session = await sessionDom.GetSessionFromCache();
        Assert.IsTrue(session.LastUsedTime > time);
    }

    [TestMethod]
    public async Task Session_AddUsage_should_update_session_LastUsedTime()
    {
        var accessPointGroupDom = await AccessPointGroupDom.Create();
        var accessTokenDom = await accessPointGroupDom.CreateAccessToken(false);

        var sessionDom = await accessTokenDom.CreateSession();
        var session = await sessionDom.GetSessionFromCache();
        Assert.IsTrue(session.LastUsedTime >= accessPointGroupDom.CreatedTime);

        // get the token again
        var time = DateTime.UtcNow;
        await Task.Delay(1000);
        await sessionDom.AddUsage(1);
        session = await sessionDom.GetSessionFromCache();
        Assert.IsTrue(session.LastUsedTime >= time);
    }

    [TestMethod]
    public async Task Session_Create_Data_Unauthorized_EndPoint()
    {
        var accessTokenClient = TestInit1.AccessTokensClient;

        // create first public token
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });

        var agentClient = TestInit1.CreateAgentClient(TestInit1.ServerId2);

        //-----------
        // check: access should grant to public token 1 by another public endpoint
        //-----------
        var sessionResponseEx =
            await agentClient.Session_Create(
                TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPointG1S2));
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);

        //-----------
        // check: access should not grant to public token 1 by private server endpoint
        //-----------
        sessionResponseEx =
            await agentClient.Session_Create(
                TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPointG2S1));
        Assert.AreEqual(SessionErrorCode.AccessError, sessionResponseEx.ErrorCode);
        Assert.IsTrue(sessionResponseEx.ErrorMessage?.Contains("Invalid EndPoint", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Session_Close()
    {
        TestInit1.AgentOptions.SessionTimeout = TimeSpan.FromSeconds(2);

        var sampleFarm1 = await SampleFarm.Create(TestInit1);
        var session = sampleFarm1.Server1.Sessions.First();
        var responseBase = await session.AddUsage();
        var lastAccessUsage = responseBase.AccessUsage;

        responseBase = await session.CloseSession();
        Assert.AreEqual(SessionErrorCode.Ok, responseBase.ErrorCode);

        //-----------
        // check
        //-----------
        responseBase = await session.AddUsage(0);
        Assert.AreEqual(SessionErrorCode.SessionClosed, responseBase.ErrorCode, "The session must be closed!");

        //-----------
        // check
        //-----------
        var responseBase2 = await session.AddUsage(10, 5);
        Assert.AreEqual(SessionErrorCode.SessionClosed, responseBase.ErrorCode, "The session must be closed!");
        Assert.AreEqual(responseBase.AccessUsage!.SentTraffic + 10, responseBase2.AccessUsage!.SentTraffic, 
            "AddUsage must work on closed a session!");
        Assert.AreEqual(responseBase.AccessUsage!.ReceivedTraffic + 5, responseBase2.AccessUsage!.ReceivedTraffic, 
            "AddUsage must work on closed a session!");

        //-----------
        // check: The Session should not exist after sync
        //-----------
        await Task.Delay(TestInit1.AgentOptions.SessionTimeout);
        await TestInit1.Sync();
        try
        {
            responseBase = await session.AddUsage(0);
            Assert.AreEqual(SessionErrorCode.SessionClosed, responseBase.ErrorCode); //it is temporary

            //todo after fixing VpnHoodServers it must throw 404
            //Assert.Fail($"{nameof(NotExistsException)} ws expected!"); 
        }
        catch (ApiException e)
        {
            Assert.AreEqual(nameof(NotExistsException), e.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Session_Bombard()
    {
        var sampleFarm1 = await SampleFarm.Create(TestInit1);
        var sampleFarm2 = await SampleFarm.Create(TestInit1);

        var createSessionTasks = new List<Task<SessionDom>>();
        for (var i = 0; i < 50; i++)
        {
            createSessionTasks.Add(sampleFarm1.Server1.AddSession(sampleFarm1.PublicToken1));
            createSessionTasks.Add(sampleFarm1.Server1.AddSession(sampleFarm1.PublicToken1));
            createSessionTasks.Add(sampleFarm1.Server2.AddSession(sampleFarm1.PublicToken2));
            createSessionTasks.Add(sampleFarm1.Server2.AddSession(sampleFarm1.PrivateToken1));
            createSessionTasks.Add(sampleFarm1.Server2.AddSession(sampleFarm1.PrivateToken2));

            createSessionTasks.Add(sampleFarm2.Server1.AddSession(sampleFarm2.PublicToken1));
            createSessionTasks.Add(sampleFarm2.Server1.AddSession(sampleFarm2.PublicToken1));
            createSessionTasks.Add(sampleFarm2.Server2.AddSession(sampleFarm2.PublicToken2));
            createSessionTasks.Add(sampleFarm2.Server2.AddSession(sampleFarm2.PrivateToken1));
            createSessionTasks.Add(sampleFarm2.Server2.AddSession(sampleFarm2.PrivateToken2));
        }

        await Task.WhenAll(createSessionTasks);
        await TestInit1.Sync();

        await sampleFarm1.Server1.AddSession(sampleFarm1.PublicToken1);
        await sampleFarm1.Server1.AddSession(sampleFarm1.PublicToken1);
        await sampleFarm1.Server1.AddSession(sampleFarm1.PrivateToken1);
        await sampleFarm1.Server2.AddSession(sampleFarm1.PublicToken1);
        await sampleFarm2.Server2.AddSession(sampleFarm2.PublicToken2);

        var tasks = sampleFarm1.Server1.Sessions.Select(x => x.AddUsage());
        tasks = tasks.Concat(sampleFarm1.Server2.Sessions.Select(x => x.AddUsage()));
        tasks = tasks.Concat(sampleFarm2.Server1.Sessions.Select(x => x.AddUsage()));
        tasks = tasks.Concat(sampleFarm2.Server2.Sessions.Select(x => x.AddUsage()));
        await Task.WhenAll(tasks);

        await TestInit1.FlushCache();
        tasks = sampleFarm1.Server1.Sessions.Select(x => x.AddUsage());
        tasks = tasks.Concat(sampleFarm1.Server2.Sessions.Select(x => x.AddUsage()));
        tasks = tasks.Concat(sampleFarm2.Server1.Sessions.Select(x => x.AddUsage()));
        tasks = tasks.Concat(sampleFarm2.Server2.Sessions.Select(x => x.AddUsage()));
        await Task.WhenAll(tasks);


        await TestInit1.Sync();
    }

    [TestMethod]
    public async Task Session_AddUsage_Public()
    {
        // create token
        var accessTokenClient = TestInit1.AccessTokensClient;
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1, IsPublic = true });

        var agentClient = TestInit1.CreateAgentClient(TestInit1.ServerId1);
        var sessionRequestEx1 = TestInit1.CreateSessionRequestEx(accessToken);
        var sessionResponseEx1 = await agentClient.Session_Create(sessionRequestEx1);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode);

        //--------------
        // check: zero usage
        //--------------
        var baseResponse = await agentClient.Session_AddUsage(
            sessionResponseEx1.SessionId, new UsageInfo
            {
                SentTraffic = 0,
                ReceivedTraffic = 0
            });
        Assert.AreEqual(0, baseResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(0, baseResponse.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

        var access = await GetAccessFromSession(sessionResponseEx1.SessionId);
        Assert.AreEqual(0, access.TotalSentTraffic);
        Assert.AreEqual(0, access.TotalReceivedTraffic);
        Assert.AreEqual(0, access.TotalTraffic);

        //-----------
        // check: add usage
        //-----------
        baseResponse = await agentClient.Session_AddUsage(sessionResponseEx1.SessionId,
            new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
        await TestInit1.FlushCache();
        Assert.AreEqual(5, baseResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, baseResponse.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

        access = await GetAccessFromSession(sessionResponseEx1.SessionId);
        Assert.AreEqual(5, access.TotalSentTraffic);
        Assert.AreEqual(10, access.TotalReceivedTraffic);
        Assert.AreEqual(15, access.TotalTraffic);

        // again
        baseResponse = await agentClient.Session_AddUsage(sessionResponseEx1.SessionId,
            new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
        await TestInit1.FlushCache();
        Assert.AreEqual(10, baseResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(20, baseResponse.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

        access = await GetAccessFromSession(sessionResponseEx1.SessionId);
        Assert.AreEqual(10, access.TotalSentTraffic);
        Assert.AreEqual(20, access.TotalReceivedTraffic);
        Assert.AreEqual(30, access.TotalTraffic);

        //-----------
        // check: add usage for client 2
        //-----------
        var sessionRequestEx2 = TestInit1.CreateSessionRequestEx(accessToken);
        var sessionResponseEx2 = await agentClient.Session_Create(sessionRequestEx2);
        baseResponse = await agentClient.Session_AddUsage(sessionResponseEx2.SessionId,
            new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
        await TestInit1.FlushCache();

        Assert.AreEqual(5, baseResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, baseResponse.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

        access = await GetAccessFromSession(sessionResponseEx2.SessionId);
        Assert.AreEqual(5, access.TotalSentTraffic);
        Assert.AreEqual(10, access.TotalReceivedTraffic);
        Assert.AreEqual(15, access.TotalTraffic);

        //-------------
        // check: add usage to client 1 after cycle
        //-------------

        //remove last cycle
        var cycleManager = TestInit1.WebApp.Services.GetRequiredService<UsageCycleService>();
        await cycleManager.DeleteCycle(cycleManager.CurrentCycleId);
        await cycleManager.UpdateCycle();

        baseResponse = await agentClient.Session_AddUsage(sessionResponseEx2.SessionId,
            new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
        Assert.AreEqual(5, baseResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, baseResponse.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

        //-------------
        // check: another Session_Create for same client should return same result
        //-------------
        sessionResponseEx2 = await agentClient.Session_Create(sessionRequestEx2);
        Assert.AreEqual(5, sessionResponseEx2.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, sessionResponseEx2.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx2.ErrorCode);

        //-------------
        // check: Session for another client should be reset too
        //-------------
        baseResponse = await agentClient.Session_AddUsage(sessionResponseEx1.SessionId,
            new UsageInfo
            {
                SentTraffic = 50,
                ReceivedTraffic = 100
            });
        await TestInit1.FlushCache();
        Assert.AreEqual(50, baseResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(100, baseResponse.AccessUsage?.ReceivedTraffic);
    }

    [TestMethod]
    public async Task Session_AddUsage_Private()
    {
        var accessTokenClient = TestInit1.AccessTokensClient;

        // create token
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1, IsPublic = false });

        var agentClient = TestInit1.CreateAgentClient();
        var sessionRequestEx1 = TestInit1.CreateSessionRequestEx(accessToken);
        var sessionResponseEx1 = await agentClient.Session_Create(sessionRequestEx1);

        //--------------
        // check: zero usage
        //--------------
        var response = await agentClient.Session_AddUsage(sessionResponseEx1.SessionId,
            new UsageInfo
            {
                SentTraffic = 0,
                ReceivedTraffic = 0
            });
        Assert.AreEqual(0, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(0, response.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode);

        Assert.AreEqual(0, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(0, response.AccessUsage?.ReceivedTraffic);

        //-----------
        // check: add usage by client 1
        //-----------
        response = await agentClient.Session_AddUsage(sessionResponseEx1.SessionId,
            new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
        await TestInit1.FlushCache();
        Assert.AreEqual(5, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, response.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode);

        var accessData = await accessTokenClient.GetAsync(TestInit1.ProjectId, accessToken.AccessTokenId);
        Assert.AreEqual(5, accessData.Access?.TotalSentTraffic);
        Assert.AreEqual(10, accessData.Access?.TotalReceivedTraffic);

        // again by client 2
        var sessionRequestEx2 = TestInit1.CreateSessionRequestEx(accessToken);
        var sessionResponseEx2 = await agentClient.Session_Create(sessionRequestEx2);
        var response2 = await agentClient.Session_AddUsage(sessionResponseEx2.SessionId,
            new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
        Assert.AreEqual(10, response2.AccessUsage?.SentTraffic);
        Assert.AreEqual(20, response2.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response2.ErrorCode);

        await TestInit1.FlushCache();
        accessData = await accessTokenClient.GetAsync(TestInit1.ProjectId, accessToken.AccessTokenId);
        Assert.AreEqual(10, accessData.Access?.TotalSentTraffic);
        Assert.AreEqual(20, accessData.Access?.TotalReceivedTraffic);
    }

    [TestMethod]
    public async Task Auto_Flush_Cache()
    {
        var testInit = await TestInit.Create(appSettings: new Dictionary<string, string?>
        {
            ["App:SaveCacheInterval"] = "00:00:00.100"
        });
        var sampler = await AccessPointGroupDom.Create(testInit);
        var sampleAccessToken = await sampler.CreateAccessToken(true);
        var sampleSession = await sampleAccessToken.CreateSession();
        await sampleSession.CloseSession();
        await Task.Delay(1000);

        Assert.IsTrue(
            await testInit.VhContext.Sessions.AnyAsync(x =>
                x.SessionId == sampleSession.SessionId && x.EndTime != null),
            "Session has not been synced yet");
    }

    [TestMethod]
    public async Task AccessUsage_Inserted()
    {
        var sample = await AccessPointGroupDom.Create(TestInit1);

        // create token
        var sampleAccessToken = await sample.CreateAccessToken(false);
        var sampleSession = await sampleAccessToken.CreateSession();
        await sampleSession.AddUsage(10051, 20051);
        await sampleSession.AddUsage(20, 30);
        await sampleSession.CloseSession();

        await TestInit1.FlushCache();

        await using var vhContext = TestInit1.Scope.ServiceProvider.GetRequiredService<VhContext>();
        var session = await vhContext.Sessions
            .Include(x => x.Access)
            .Include(x => x.Access!.AccessToken)
            .SingleAsync(x => x.SessionId == sampleSession.SessionId);

        var deviceClient = TestInit1.DevicesClient;
        var deviceData = await deviceClient.GetAsync(TestInit1.ProjectId, session.DeviceId);

        Assert.AreEqual(sampleAccessToken.AccessTokenId, session.Access?.AccessTokenId);
        Assert.AreEqual(sampleSession.SessionRequestEx.ClientInfo.ClientId, deviceData.Device.ClientId);
        Assert.AreEqual(IPAddressUtil.Anonymize(sampleSession.SessionRequestEx.ClientIp!).ToString(), session.DeviceIp);
        Assert.AreEqual(sampleSession.SessionRequestEx.ClientInfo.ClientVersion, session.ClientVersion);

        // check sync
        await TestInit1.Sync();
        await using var vhReportContext = TestInit1.Scope.ServiceProvider.GetRequiredService<VhReportContext>();
        var accessUsage = await vhReportContext.AccessUsages
            .OrderByDescending(x => x.AccessUsageId)
            .FirstAsync(x => x.SessionId == sampleSession.SessionId);

        Assert.IsNotNull(accessUsage);
        Assert.AreEqual(10071, accessUsage.CycleSentTraffic);
        Assert.AreEqual(20081, accessUsage.CycleReceivedTraffic);
        Assert.AreEqual(10071, accessUsage.TotalSentTraffic);
        Assert.AreEqual(20081, accessUsage.TotalReceivedTraffic);
        Assert.AreEqual(session.ServerId, accessUsage.ServerId);
        Assert.AreEqual(session.DeviceId, accessUsage.DeviceId);
        Assert.AreEqual(session.Access!.AccessTokenId, accessUsage.AccessTokenId);
        Assert.AreEqual(session.Access!.AccessToken?.AccessPointGroupId, accessUsage.AccessPointGroupId);
    }

    [TestMethod]
    public async Task Session_Create_Status_SuppressToOther()
    {
        var sampler = await AccessPointGroupDom.Create();
        var accessToken = await sampler.TestInit.AccessTokensClient.CreateAsync(sampler.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = sampler.AccessPointGroupId,
                MaxDevice = 2
            });

        var sampleAccessToken = new AccessTokenDom(sampler.TestInit, accessToken);
        var sampleSession1 = await sampleAccessToken.CreateSession();
        await sampleAccessToken.CreateSession();
        var sampleSession3 = await sampleAccessToken.CreateSession();
        Assert.AreEqual(SessionSuppressType.Other, sampleSession3.SessionResponseEx.SuppressedTo);

        var res = await sampleSession1.AddUsage(0);
        Assert.AreEqual(SessionSuppressType.Other, res.SuppressedBy);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, res.ErrorCode);

        // Check after Flush
        await sampler.TestInit.FlushCache();
        await sampler.TestInit.CacheService.InvalidateSessions();
        res = await sampleSession1.AddUsage(0);
        Assert.AreEqual(SessionSuppressType.Other, res.SuppressedBy);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, res.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_Status_SuppressToYourself()
    {
        var sampler = await AccessPointGroupDom.Create();
        var accessToken = await sampler.TestInit.AccessTokensClient.CreateAsync(sampler.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = sampler.AccessPointGroupId,
                MaxDevice = 2
            });

        var sampleAccessToken = new AccessTokenDom(sampler.TestInit, accessToken);
        var clientId = Guid.NewGuid();

        var sampleSession1 = await sampleAccessToken.CreateSession(clientId);
        await sampleAccessToken.CreateSession(clientId);

        var sampleSession = await sampleAccessToken.CreateSession(clientId);
        Assert.AreEqual(SessionSuppressType.YourSelf, sampleSession.SessionResponseEx.SuppressedTo);

        var res = await sampleSession1.AddUsage(0);
        Assert.AreEqual(SessionSuppressType.YourSelf, res.SuppressedBy);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, res.ErrorCode);
    }
}