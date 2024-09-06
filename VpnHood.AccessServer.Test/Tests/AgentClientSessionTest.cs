using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Services;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using Token = VpnHood.Common.Token;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AgentClientSessionTest
{
    [TestMethod]
    public async Task Session_Create_Status_TrafficOverflow()
    {
        using var farm = await ServerFarmDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(new AccessTokenCreateParams {
            MaxTraffic = 14
        });

        //-----------
        // check: add usage
        //-----------
        var sessionDom = await accessTokenDom.CreateSession();
        var sessionResponse = await sessionDom.AddUsage(5, 10);
        await farm.TestApp.FlushCache();
        Assert.AreEqual(5, sessionResponse.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(10, sessionResponse.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.AccessTrafficOverflow, sessionResponse.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_Status_No_TrafficOverflow_when_maxTraffic_is_zero()
    {
        using var farm = await ServerFarmDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(new AccessTokenCreateParams {
            MaxTraffic = 0
        });

        //-----------
        // check: add usage
        //-----------
        var sessionDom = await accessTokenDom.CreateSession();
        var sessionResponse = await sessionDom.AddUsage(5, 10);
        Assert.AreEqual(5, sessionResponse.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(10, sessionResponse.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponse.ErrorCode);
    }

    [TestMethod]
    public async Task Push_token_to_client()
    {
        var farmOptions = new ServerFarmCreateParams();
        using var farm = await ServerFarmDom.Create(createParams: farmOptions, serverCount: 0);
        await farm.AddNewServer(configure: true, sendStatus: true);
        await farm.AddNewServer(configure: true, sendStatus: true);
        var accessTokenDom = await farm.CreateAccessToken();

        // ------------
        // Check: all server ready
        // ------------
        var sessionDom = await accessTokenDom.CreateSession();
        Assert.IsNotNull(sessionDom.SessionResponseEx.AccessKey);

        // ------------
        // Check: a when server is not ready
        // ------------
        var badServer = await farm.AddNewServer();

        badServer.Server.AccessPoints.First(x => x.AccessPointMode == AccessPointMode.Public).AccessPointMode =
            AccessPointMode.PublicInToken;
        await badServer.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf { Value = badServer.Server.AccessPoints }
        });

        sessionDom = await accessTokenDom.CreateSession();
        Assert.IsNull(sessionDom.SessionResponseEx.AccessKey,
            "Must be null when a server with PublicInToken is not ready.");
    }

    [TestMethod]
    public async Task Session_Create_success()
    {
        var farmOptions = new ServerFarmCreateParams();
        using var farm = await ServerFarmDom.Create(createParams: farmOptions);
        var accessTokenDom = await farm.CreateAccessToken(new AccessTokenCreateParams {
            MaxTraffic = 100,
            ExpirationTime = new DateTime(2040, 1, 1),
            Lifetime = 0,
            MaxDevice = 22
        });

        // add a token repo to farm
        var tokenRepoUrl = new Uri("http://localhost:5000/api/v1/token-repo");
        await farm.TestApp.FarmTokenReposClient.CreateAsync(farm.ProjectId, farm.ServerFarmId,
            new FarmTokenRepoCreateParams {
                RepoName = "TestRepo",
                UploadMethod = "PUT",
                PublishUrl = tokenRepoUrl,
            });

        var beforeUpdateTime = DateTime.UtcNow.AddSeconds(-1);
        var sessionDom = await accessTokenDom.CreateSession();
        var accessTokenData = await accessTokenDom.Reload();

        var sessionResponseEx = sessionDom.SessionResponseEx;
        Assert.IsNotNull(sessionResponseEx.AccessUsage);
        Assert.IsTrue(sessionResponseEx.SessionId > 0);
        Assert.AreEqual(new DateTime(2040, 1, 1), sessionResponseEx.AccessUsage!.ExpirationTime);
        Assert.AreEqual(22, sessionResponseEx.AccessUsage.MaxClientCount);
        Assert.AreEqual(100, sessionResponseEx.AccessUsage.MaxTraffic);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.Traffic.Received);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.Traffic.Sent);
        Assert.IsNotNull(sessionResponseEx.AccessKey);
        Assert.AreEqual(tokenRepoUrl.ToString(),
            Token.FromAccessKey(sessionResponseEx.AccessKey).ServerToken.Urls?.FirstOrDefault());
        Assert.IsNotNull(sessionResponseEx.SessionKey);
        Assert.IsTrue(accessTokenData.Access!.CreatedTime >= beforeUpdateTime);
        Assert.IsTrue(accessTokenData.Access!.CreatedTime >= beforeUpdateTime);

        // check Device id and its properties are created 
        var clientInfo = sessionDom.SessionRequestEx.ClientInfo;
        var device = await farm.TestApp.DevicesClient.FindByClientIdAsync(farm.TestApp.ProjectId, clientInfo.ClientId);
        Assert.AreEqual(clientInfo.ClientId, device.ClientId);
        Assert.AreEqual(clientInfo.UserAgent, device.UserAgent);
        Assert.AreEqual(clientInfo.ClientVersion, device.ClientVersion);

        // check updating same client
        clientInfo = new ClientInfo {
            ClientId = clientInfo.ClientId,
            UserAgent = "userAgent2",
            ClientVersion = "200.0.0",
            ProtocolVersion = clientInfo.ProtocolVersion
        };
        sessionDom = await accessTokenDom.CreateSession(clientInfo: clientInfo);
        var sessionRequestEx = sessionDom.SessionRequestEx;
        sessionRequestEx.ClientIp = farm.TestApp.NewIpV4();
        device = await farm.TestApp.DevicesClient.FindByClientIdAsync(farm.TestApp.ProjectId, clientInfo.ClientId);
        Assert.AreEqual(sessionRequestEx.ClientInfo.UserAgent, device.UserAgent);
        Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, device.ClientVersion);
    }

    private static async Task<AccessModel> GetAccessFromSession(SessionDom sessionDom)
    {
        await using var scope = sessionDom.TestApp.WebApp.Services.CreateAsyncScope();
        var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        var session = await vhContext.Sessions
            .Include(x => x.Access)
            .SingleAsync(x => x.SessionId == sessionDom.SessionId);

        return session.Access!;
    }

    [TestMethod]
    public async Task Session_Get()
    {
        var serverFarmDom = await ServerFarmDom.Create();
        var accessTokenDom = await serverFarmDom.CreateAccessToken();

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
        var serverFarmDom = await ServerFarmDom.Create();
        var accessTokenDom = await serverFarmDom.CreateAccessToken();

        var sessionDom = await accessTokenDom.CreateSession();
        var session = await sessionDom.GetSessionFromCache();
        Assert.IsTrue(session.LastUsedTime >= serverFarmDom.CreatedTime);

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
        var serverFarmDom = await ServerFarmDom.Create();
        var accessTokenDom = await serverFarmDom.CreateAccessToken();

        var sessionDom = await accessTokenDom.CreateSession();
        var session = await sessionDom.GetSessionFromCache();
        Assert.IsTrue(session.LastUsedTime >= serverFarmDom.CreatedTime);

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
        var farm1 = await ServerFarmDom.Create();
        var serverDom11 = await farm1.AddNewServer();
        var serverDom12 = await farm1.AddNewServer();
        var accessTokenDom11 = await farm1.CreateAccessToken(true);

        var farm2 = await ServerFarmDom.Create(farm1.TestApp);
        var serverDom21 = await farm2.AddNewServer();
        var serverDom22 = await farm2.AddNewServer();
        var accessTokenDom21 = await farm2.CreateAccessToken(true);

        //-----------
        // check: access should grant to public token by any server
        //-----------
        var session = await serverDom11.CreateSession(accessTokenDom11.AccessToken);
        Assert.AreEqual(SessionErrorCode.Ok, session.SessionResponseEx.ErrorCode);

        session = await serverDom12.CreateSession(accessTokenDom11.AccessToken);
        Assert.AreEqual(SessionErrorCode.Ok, session.SessionResponseEx.ErrorCode);

        session = await serverDom21.CreateSession(accessTokenDom21.AccessToken);
        Assert.AreEqual(SessionErrorCode.Ok, session.SessionResponseEx.ErrorCode);

        session = await serverDom22.CreateSession(accessTokenDom21.AccessToken);
        Assert.AreEqual(SessionErrorCode.Ok, session.SessionResponseEx.ErrorCode);

        session = await serverDom21.CreateSession(accessTokenDom11.AccessToken, assertError: false);
        Assert.AreEqual(SessionErrorCode.AccessError, session.SessionResponseEx.ErrorCode,
            "access should not grant by another farm token");
    }

    [TestMethod]
    public async Task Session_Close()
    {
        var testApp = await TestApp.Create();
        testApp.AgentTestApp.AgentOptions.SessionPermanentlyTimeout = TimeSpan.FromSeconds(2);
        testApp.AgentTestApp.AgentOptions.SessionTemporaryTimeout =
            TimeSpan.FromSeconds(2); //should not be less than PermanentlyTimeout
        using var sampleFarm1 = await SampleFarm.Create(testApp);
        var session = sampleFarm1.Server1.Sessions.First();
        var responseBase = await session.CloseSession();
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
        Assert.AreEqual(responseBase.AccessUsage!.Traffic.Sent + 10, responseBase2.AccessUsage!.Traffic.Sent,
            "AddUsage must work on closed a session!");
        Assert.AreEqual(responseBase.AccessUsage!.Traffic.Received + 5, responseBase2.AccessUsage!.Traffic.Received,
            "AddUsage must work on closed a session!");

        //-----------
        // check: The Session should not exist after sync
        //-----------
        await Task.Delay(
            testApp.AgentTestApp.AgentOptions.SessionPermanentlyTimeout.Add(TimeSpan.FromMilliseconds(50)));
        await testApp.Sync();
        await VhTestUtil.AssertNotExistsException(session.AddUsage(0));
    }

    [TestMethod]
    public async Task Session_Bombard()
    {
        var testApp = await TestApp.Create();
        using var sampleFarm1 = await SampleFarm.Create(testApp);
        using var sampleFarm2 = await SampleFarm.Create(testApp);

        var createSessionTasks = new List<Task<SessionDom>>();
        for (var i = 0; i < 50; i++) {
            createSessionTasks.Add(sampleFarm1.Server1.CreateSession(sampleFarm1.PublicToken1));
            createSessionTasks.Add(sampleFarm1.Server1.CreateSession(sampleFarm1.PublicToken1));
            createSessionTasks.Add(sampleFarm1.Server2.CreateSession(sampleFarm1.PublicToken2));
            createSessionTasks.Add(sampleFarm1.Server2.CreateSession(sampleFarm1.PrivateToken1));
            createSessionTasks.Add(sampleFarm1.Server2.CreateSession(sampleFarm1.PrivateToken2));

            createSessionTasks.Add(sampleFarm2.Server1.CreateSession(sampleFarm2.PublicToken1));
            createSessionTasks.Add(sampleFarm2.Server1.CreateSession(sampleFarm2.PublicToken1));
            createSessionTasks.Add(sampleFarm2.Server2.CreateSession(sampleFarm2.PublicToken2));
            createSessionTasks.Add(sampleFarm2.Server2.CreateSession(sampleFarm2.PrivateToken1));
            createSessionTasks.Add(sampleFarm2.Server2.CreateSession(sampleFarm2.PrivateToken2));
        }

        await Task.WhenAll(createSessionTasks);
        await testApp.Sync();

        await sampleFarm1.Server1.CreateSession(sampleFarm1.PublicToken1);
        await sampleFarm1.Server1.CreateSession(sampleFarm1.PublicToken1);
        await sampleFarm1.Server1.CreateSession(sampleFarm1.PrivateToken1);
        await sampleFarm1.Server2.CreateSession(sampleFarm1.PublicToken1);
        await sampleFarm2.Server2.CreateSession(sampleFarm2.PublicToken2);

        var tasks = sampleFarm1.Server1.Sessions.Select(x => x.AddUsage());
        tasks = tasks.Concat(sampleFarm1.Server2.Sessions.Select(x => x.AddUsage()));
        tasks = tasks.Concat(sampleFarm2.Server1.Sessions.Select(x => x.AddUsage()));
        tasks = tasks.Concat(sampleFarm2.Server2.Sessions.Select(x => x.AddUsage()));
        await Task.WhenAll(tasks);

        await testApp.FlushCache();
        tasks = sampleFarm1.Server1.Sessions.Select(x => x.AddUsage());
        tasks = tasks.Concat(sampleFarm1.Server2.Sessions.Select(x => x.AddUsage()));
        tasks = tasks.Concat(sampleFarm2.Server1.Sessions.Select(x => x.AddUsage()));
        tasks = tasks.Concat(sampleFarm2.Server2.Sessions.Select(x => x.AddUsage()));
        await Task.WhenAll(tasks);


        await testApp.Sync();
    }

    [TestMethod]
    public async Task Session_AddUsage_Public()
    {
        using var farm = await ServerFarmDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true);
        var sessionDom1 = await accessTokenDom.CreateSession();

        //--------------
        // check: zero usage
        //--------------
        var response = await sessionDom1.AddUsage(0);
        Assert.AreEqual(0, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(0, response.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        var access = await GetAccessFromSession(sessionDom1);
        Assert.AreEqual(0, access.TotalSentTraffic);
        Assert.AreEqual(0, access.TotalReceivedTraffic);
        Assert.AreEqual(0, access.TotalTraffic);

        //-----------
        // check: add usage
        //-----------
        response = await sessionDom1.AddUsage(5, 10);
        await farm.TestApp.FlushCache();
        Assert.AreEqual(5, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(10, response.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        access = await GetAccessFromSession(sessionDom1);
        Assert.AreEqual(5, access.TotalSentTraffic);
        Assert.AreEqual(10, access.TotalReceivedTraffic);
        Assert.AreEqual(15, access.TotalTraffic);

        // again
        response = await sessionDom1.AddUsage(5, 10);
        await farm.TestApp.FlushCache();
        Assert.AreEqual(10, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(20, response.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        access = await GetAccessFromSession(sessionDom1);
        Assert.AreEqual(10, access.TotalSentTraffic);
        Assert.AreEqual(20, access.TotalReceivedTraffic);
        Assert.AreEqual(30, access.TotalTraffic);

        //-----------
        // check: add usage for client 2
        //-----------
        var sessionDom2 = await accessTokenDom.CreateSession();
        response = await sessionDom2.AddUsage(5, 10);
        await farm.TestApp.FlushCache();

        Assert.AreEqual(5, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(10, response.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        access = await GetAccessFromSession(sessionDom2);
        Assert.AreEqual(5, access.TotalSentTraffic);
        Assert.AreEqual(10, access.TotalReceivedTraffic);
        Assert.AreEqual(15, access.TotalTraffic);

        //-------------
        // check: add usage to client 1 after cycle
        //-------------

        //remove last cycle
        var cycleManager = farm.TestApp.Scope.ServiceProvider.GetRequiredService<UsageCycleService>();
        await cycleManager.DeleteCycle(cycleManager.CurrentCycleId);
        await cycleManager.UpdateCycle();

        response = await sessionDom2.AddUsage(5, 10);
        Assert.AreEqual(5, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(10, response.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        //-------------
        // check: another Session_Create for same client should return same result
        //-------------
        var sessionDom3 =
            await accessTokenDom.CreateSession(clientId: sessionDom2.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(5, sessionDom3.SessionResponseEx.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(10, sessionDom3.SessionResponseEx.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom3.SessionResponseEx.ErrorCode);

        //-------------
        // check: Session for another client should be reset too
        //-------------
        response = await sessionDom1.AddUsage(50, 100);
        await farm.TestApp.FlushCache();
        Assert.AreEqual(50, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(100, response.AccessUsage?.Traffic.Received);
    }

    [TestMethod]
    public async Task Session_AddUsage_Private()
    {
        using var farm = await ServerFarmDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(isPublic: false);
        var sessionDom1 = await accessTokenDom.CreateSession();

        //--------------
        // check: zero usage
        //--------------
        var response = await sessionDom1.AddUsage(0);
        Assert.AreEqual(0, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(0, response.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        //-----------
        // check: add usage by client 1
        //-----------
        response = await sessionDom1.AddUsage(5, 10);
        await farm.TestApp.FlushCache();
        Assert.AreEqual(5, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(10, response.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        var accessData = await accessTokenDom.Reload();
        Assert.AreEqual(5, accessData.Access?.TotalSentTraffic);
        Assert.AreEqual(10, accessData.Access?.TotalReceivedTraffic);

        //-----------
        // check: add usage by client 2
        //-----------
        var sessionDom2 = await accessTokenDom.CreateSession();
        var response2 = await sessionDom2.AddUsage(5, 10);
        Assert.AreEqual(10, response2.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(20, response2.AccessUsage?.Traffic.Received);
        Assert.AreEqual(SessionErrorCode.Ok, response2.ErrorCode);

        await farm.TestApp.FlushCache();
        accessData = await accessTokenDom.Reload();
        Assert.AreEqual(10, accessData.Access?.TotalSentTraffic);
        Assert.AreEqual(20, accessData.Access?.TotalReceivedTraffic);
    }

    [TestMethod]
    public async Task AccessUsage_Inserted()
    {
        using var farm = await ServerFarmDom.Create();

        // create token
        var sampleAccessToken = await farm.CreateAccessToken();
        var sessionDom = await sampleAccessToken.CreateSession();
        await sessionDom.AddUsage(10051, 20051);
        await sessionDom.AddUsage(20, 30);
        await sessionDom.CloseSession();

        await farm.TestApp.FlushCache();

        var session = await farm.TestApp.VhContext.Sessions
            .Include(x => x.Access)
            .Include(x => x.Access!.AccessToken)
            .SingleAsync(x => x.SessionId == sessionDom.SessionId);

        var deviceData = await farm.TestApp.DevicesClient.GetAsync(farm.ProjectId, session.DeviceId);

        Assert.AreEqual(sampleAccessToken.AccessTokenId, session.Access?.AccessTokenId);
        Assert.AreEqual(sessionDom.SessionRequestEx.ClientInfo.ClientId, deviceData.Device.ClientId);
        Assert.AreEqual(IPAddressUtil.Anonymize(sessionDom.SessionRequestEx.ClientIp!).ToString(), session.DeviceIp);
        Assert.AreEqual(sessionDom.SessionRequestEx.ClientInfo.ClientVersion, session.ClientVersion);

        // check sync
        await farm.TestApp.Sync();

        var accessUsage = await farm.TestApp.VhReportContext.AccessUsages
            .OrderByDescending(x => x.AccessUsageId)
            .FirstAsync(x => x.SessionId == sessionDom.SessionId);

        Assert.IsNotNull(accessUsage);
        Assert.AreEqual(10071, accessUsage.CycleSentTraffic);
        Assert.AreEqual(20081, accessUsage.CycleReceivedTraffic);
        Assert.AreEqual(10071, accessUsage.TotalSentTraffic);
        Assert.AreEqual(20081, accessUsage.TotalReceivedTraffic);
        Assert.AreEqual(session.ServerId, accessUsage.ServerId);
        Assert.AreEqual(session.DeviceId, accessUsage.DeviceId);
        Assert.AreEqual(session.Access!.AccessTokenId, accessUsage.AccessTokenId);
        Assert.AreEqual(session.Access!.AccessToken?.ServerFarmId, accessUsage.ServerFarmId);
    }

    [TestMethod]
    public async Task Session_Create_Status_SuppressToOther()
    {
        var sampler = await ServerFarmDom.Create();
        var accessToken = await sampler.TestApp.AccessTokensClient.CreateAsync(sampler.ProjectId,
            new AccessTokenCreateParams {
                ServerFarmId = sampler.ServerFarmId,
                MaxDevice = 2
            });

        var sampleAccessToken = new AccessTokenDom(sampler.TestApp, accessToken);
        var sampleSession1 = await sampleAccessToken.CreateSession();
        await sampleAccessToken.CreateSession();
        var sampleSession3 = await sampleAccessToken.CreateSession();
        Assert.AreEqual(SessionSuppressType.Other, sampleSession3.SessionResponseEx.SuppressedTo);

        var res = await sampleSession1.AddUsage(0);
        Assert.AreEqual(SessionSuppressType.Other, res.SuppressedBy);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, res.ErrorCode);

        // Check after Flush
        await sampler.TestApp.FlushCache();
        await sampler.TestApp.AgentTestApp.CacheService.InvalidateSessions();
        res = await sampleSession1.AddUsage(0);
        Assert.AreEqual(SessionSuppressType.Other, res.SuppressedBy);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, res.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_Status_SuppressToYourself()
    {
        var sampler = await ServerFarmDom.Create();
        var accessToken = await sampler.TestApp.AccessTokensClient.CreateAsync(sampler.ProjectId,
            new AccessTokenCreateParams {
                ServerFarmId = sampler.ServerFarmId,
                MaxDevice = 2
            });

        var sampleAccessToken = new AccessTokenDom(sampler.TestApp, accessToken);
        var clientId = Guid.NewGuid();

        var sampleSession1 = await sampleAccessToken.CreateSession(clientId);
        await sampleAccessToken.CreateSession(clientId);

        var sessionDom = await sampleAccessToken.CreateSession(clientId);
        Assert.AreEqual(SessionSuppressType.YourSelf, sessionDom.SessionResponseEx.SuppressedTo);

        var res = await sampleSession1.AddUsage(0);
        Assert.AreEqual(SessionSuppressType.YourSelf, res.SuppressedBy);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, res.ErrorCode);
    }
}