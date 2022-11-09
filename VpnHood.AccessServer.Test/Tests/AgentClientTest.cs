using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Services;
using VpnHood.AccessServer.Test.Sampler;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AgentClientTest : ClientTest
{
    [TestMethod]
    public async Task Session_Create_Status_Expired()
    {
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);

        // create accessToken
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                EndTime = new DateTime(1900, 1, 1)
            });
        var agentClient = TestInit1.CreateAgentClient();

        var sessionResponseEx = await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(accessToken));
        Assert.AreEqual(SessionErrorCode.AccessExpired, sessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_Status_TrafficOverflow()
    {
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);

        // create accessToken
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
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);

        // create accessToken
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
    public async Task Session_Create_set_expirationTime_first_use()
    {
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);

        // create token
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                EndTime = null,
                Lifetime = 30
            });
        var agentClient = TestInit1.CreateAgentClient();

        //-----------
        // check: add usage
        //-----------
        var sessionResponseEx =
            await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(accessToken));
        Assert.IsNotNull(sessionResponseEx.AccessUsage);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.SentTraffic);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.ReceivedTraffic);
        Assert.IsNotNull(sessionResponseEx.AccessUsage.ExpirationTime);
        Assert.IsTrue((sessionResponseEx.AccessUsage.ExpirationTime.Value - DateTime.UtcNow.AddDays(30)).TotalSeconds <
                      10);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Get_should_update_accessedTime()
    {
        // create a session for token
        var agentClient = TestInit1.CreateAgentClient();
        var sessionRequestEx = TestInit1.CreateSessionRequestEx(TestInit1.AccessToken1);
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);

        // get the token again
        var time = DateTime.UtcNow;
        await Task.Delay(100);
        var sessionResponseEx2 =
            await agentClient.Session_Get(sessionResponseEx.SessionId, sessionRequestEx.HostEndPoint, null);
        Assert.AreEqual(sessionResponseEx.ErrorCode, sessionResponseEx2.ErrorCode);
        Assert.AreEqual(sessionResponseEx.SessionId, sessionResponseEx2.SessionId);
        CollectionAssert.AreEqual(sessionResponseEx.SessionKey, sessionResponseEx2.SessionKey);

        // ------
        // Check Access Time is modified
        // ------
        var session = await TestInit1.AgentCacheClient.GetSession(sessionResponseEx.SessionId);
        Assert.IsTrue(session.AccessedTime > time);
    }

    [TestMethod]
    public async Task Session_AddUsage_should_update_accessedTimes()
    {

        // create a session for token
        var agentClient = TestInit1.CreateAgentClient();
        var sessionRequestEx = TestInit1.CreateSessionRequestEx(TestInit1.AccessToken1);
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        var dateTime = DateTime.UtcNow;

        // addUsage
        Task.Delay(100).Wait();
        await agentClient.Session_AddUsage(sessionResponseEx.SessionId, new UsageInfo());
        var session = await TestInit1.AgentCacheClient.GetSession(sessionResponseEx.SessionId);
        Assert.IsTrue(session.AccessedTime > dateTime, "Session AccessTime is not updated.");
        Assert.IsTrue(session.Access!.AccessedTime > dateTime, "Access AccessTime is not updated.");
    }

    [TestMethod]
    public async Task Session_Create_should_not_reset_expiration_Time()
    {
        var expectedExpirationTime = DateTime.UtcNow.AddDays(10).Date;

        // create token
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId2,
                EndTime = expectedExpirationTime,
                Lifetime = 30
            });

        var agentClient = TestInit1.CreateAgentClient();
        var sessionResponseEx =
            await agentClient.Session_Create(
                TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPointG2S1));
        Assert.AreEqual(expectedExpirationTime, sessionResponseEx.AccessUsage?.ExpirationTime);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_success()
    {
        // create token
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                MaxTraffic = 100,
                EndTime = new DateTime(2040, 1, 1),
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
        Assert.AreEqual(new DateTime(2040, 1, 1), sessionResponseEx.AccessUsage.ExpirationTime);
        Assert.AreEqual(22, sessionResponseEx.AccessUsage.MaxClientCount);
        Assert.AreEqual(100, sessionResponseEx.AccessUsage.MaxTraffic);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.ReceivedTraffic);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.SentTraffic);
        Assert.IsNotNull(sessionResponseEx.SessionKey);
        Assert.IsTrue(accessTokenData.Access!.CreatedTime >= beforeUpdateTime);
        Assert.IsTrue(accessTokenData.Access!.CreatedTime >= beforeUpdateTime);

        // check Device id and its properties are created 
        var deviceClient = new DeviceClient(TestInit1.Http);
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

    private async Task<Models.Access> GetAccessFromSession(long sessionId)
    {
        await using var scope = TestInit1.WebApp.Services.CreateAsyncScope();
        await using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        var session = await vhContext.Sessions
            .Include(x => x.Access)
            .SingleAsync(x => x.SessionId == sessionId);
        return session.Access!;
    }

    [TestMethod]
    public async Task Session_Create_Data_Unauthorized_EndPoint()
    {
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);

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
        Assert.AreEqual(SessionErrorCode.GeneralError, sessionResponseEx.ErrorCode);
        Assert.IsTrue(sessionResponseEx.ErrorMessage?.Contains("Invalid EndPoint", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Session_Close()
    {
        TestInit1.AgentOptions.SessionTimeout = TimeSpan.FromSeconds(2);
        TestInit1.AgentOptions.SessionCacheTimeout = TimeSpan.FromSeconds(1);

        var sampleFarm1 = await SampleFarm.Create(TestInit1);
        var session = sampleFarm1.Server1.Sessions.First();
        var responseBase = await session.AddUsage(100);
        var lastAccessUsage = responseBase.AccessUsage;

        responseBase = await session.CloseSession();
        Assert.AreEqual(SessionErrorCode.Ok, responseBase.ErrorCode);

        //-----------
        // check
        //-----------
        responseBase = await session.AddUsage(0);
        Assert.AreEqual(SessionErrorCode.SessionClosed, responseBase.ErrorCode, "The session is not closed!");

        //-----------
        // check
        //-----------
        responseBase = await session.AddUsage(100);
        Assert.AreEqual(SessionErrorCode.SessionClosed, responseBase.ErrorCode, "The session is not closed!");
        Assert.AreEqual(lastAccessUsage?.ReceivedTraffic, responseBase.AccessUsage?.ReceivedTraffic,
            "usage should not be changed after closing a session");

        //-----------
        // check: The Session should not exist after sync
        //-----------
        await Task.Delay(TestInit1.AgentOptions.SessionCacheTimeout);
        await TestInit1.Sync();
        try
        {
            await session.AddUsage(0);
            Assert.Fail($"{nameof(NotExistsException)} ws expected!");
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

        var createSessionTasks = new List<Task<SampleSession>>();
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

        var tasks = sampleFarm1.Server1.Sessions.Select(x => x.AddUsage(100));
        tasks = tasks.Concat(sampleFarm1.Server2.Sessions.Select(x => x.AddUsage(100)));
        tasks = tasks.Concat(sampleFarm2.Server1.Sessions.Select(x => x.AddUsage(100)));
        tasks = tasks.Concat(sampleFarm2.Server2.Sessions.Select(x => x.AddUsage(100)));
        await Task.WhenAll(tasks);

        await TestInit1.FlushCache();
        tasks = sampleFarm1.Server1.Sessions.Select(x => x.AddUsage(100));
        tasks = tasks.Concat(sampleFarm1.Server2.Sessions.Select(x => x.AddUsage(100)));
        tasks = tasks.Concat(sampleFarm2.Server1.Sessions.Select(x => x.AddUsage(100)));
        tasks = tasks.Concat(sampleFarm2.Server2.Sessions.Select(x => x.AddUsage(100)));
        await Task.WhenAll(tasks);


        await TestInit1.Sync();
    }

    [TestMethod]
    public async Task Session_AddUsage_Public()
    {
        // create token
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);
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
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);

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
        await TestInit1.FlushCache();
        Assert.AreEqual(10, response2.AccessUsage?.SentTraffic);
        Assert.AreEqual(20, response2.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response2.ErrorCode);

        accessData = await accessTokenClient.GetAsync(TestInit1.ProjectId, accessToken.AccessTokenId);
        Assert.AreEqual(10, accessData.Access?.TotalSentTraffic);
        Assert.AreEqual(20, accessData.Access?.TotalReceivedTraffic);
    }

    [TestMethod]
    public async Task GetCertificateData()
    {
        // create new AccessPoint
        var privateEp = new IPEndPoint(await TestInit1.NewIpV4(), 4443);
        var publicEp1 = new IPEndPoint(await TestInit1.NewIpV4(), 4443);
        var publicEp2 = new IPEndPoint(await TestInit1.NewIpV4(), 4443);
        var accessPointClient = new AccessPointClient(TestInit1.Http);
        await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1,
                IpAddress = publicEp1.Address.ToString(),
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                TcpPort = publicEp1.Port,
                AccessPointMode = AccessPointMode.Public,
                IsListen = true
            });

        await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1,
                IpAddress = publicEp2.Address.ToString(),
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                TcpPort = publicEp2.Port,
                AccessPointMode = AccessPointMode.PublicInToken,
                IsListen = false
            });

        await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1,
                IpAddress = privateEp.Address.ToString(),
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                TcpPort = privateEp.Port,
                AccessPointMode = AccessPointMode.Private,
                IsListen = true
            });


        //-----------
        // check: get certificate by publicIp
        //-----------
        var agentClient = TestInit1.CreateAgentClient();
        var certBuffer = await agentClient.GetSslCertificateData(publicEp1);
        var certificate = new X509Certificate2(certBuffer);
        Assert.AreEqual(TestInit1.PublicServerDns, certificate.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // check: get certificate by privateIp
        //-----------
        certBuffer = await agentClient.GetSslCertificateData(privateEp);
        certificate = new X509Certificate2(certBuffer);
        Assert.AreEqual(TestInit1.PublicServerDns, certificate.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // check: check not found
        //-----------
        try
        {
            await agentClient.GetSslCertificateData(publicEp2);
            Assert.Fail("NotExistsException expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Server_UpdateStatus()
    {
        var agentClient1 = TestInit1.CreateAgentClient(TestInit1.ServerId1);
        await agentClient1.Server_UpdateStatus(new ServerStatus { SessionCount = 10 });

        var agentClient2 = TestInit1.CreateAgentClient(TestInit1.ServerId2);
        await agentClient2.Server_UpdateStatus(new ServerStatus { SessionCount = 20 });

        var serverData1 = await TestInit1.ServerClient.GetAsync(TestInit1.ProjectId, TestInit1.ServerId1);
        Assert.AreEqual(serverData1.Server.ServerStatus?.SessionCount, 10);

        var serverData2 = await TestInit1.ServerClient.GetAsync(TestInit1.ProjectId, TestInit1.ServerId2);
        Assert.AreEqual(serverData2.Server.ServerStatus?.SessionCount, 20);
    }

    [TestMethod]
    public async Task Auto_Flush_Cache()
    {
        var testInit = await TestInit.Create(appSettings: new Dictionary<string, string?>
        {
            ["App:SaveCacheInterval"] = "00:00:00.100"
        });
        var sampler = await SampleAccessPointGroup.Create(testInit);
        var sampleAccessToken = await sampler.CreateAccessToken(true);
        var sampleSession = await sampleAccessToken.CreateSession();
        await sampleSession.CloseSession();
        await Task.Delay(1000);

        Assert.IsTrue(await testInit.VhContext.Sessions.AnyAsync(x => x.SessionId == sampleSession.SessionId && x.EndTime != null), 
            "Session has not been synced yet");
    }


    [TestMethod]
    public async Task AccessUsage_Inserted()
    {
        var sample = await SampleAccessPointGroup.Create(TestInit1);

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

        var deviceClient = new DeviceClient(TestInit1.Http);
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
    public async Task Configure()
    {
        // create serverInfo
        var serverClient = new ServerClient(TestInit1.Http);
        var serverId = (await serverClient.CreateAsync(TestInit1.ProjectId,
            new ServerCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 })).ServerId;
        var dateTime = DateTime.UtcNow.AddSeconds(-1);

        // create serverInfo
        var agentClient1 = TestInit1.CreateAgentClient(serverId);
        var serverInfo1 = await TestInit1.NewServerInfo();
        var publicIp = await TestInit1.NewIpV6();
        serverInfo1.PrivateIpAddresses = new[] { publicIp, (await TestInit1.NewIpV4()), (await TestInit1.NewIpV6()) };
        serverInfo1.PublicIpAddresses = new[] { publicIp, (await TestInit1.NewIpV4()), (await TestInit1.NewIpV6()) };

        //Configure
        await agentClient1.Server_Configure(serverInfo1);
        await TestInit1.Sync();
        var serverConfig = await agentClient1.Server_Configure(serverInfo1); // last status will not be synced
        await TestInit1.Sync();

        var serverData = await serverClient.GetAsync(TestInit1.ProjectId, serverId);
        var server = serverData.Server;
        var serverStatusEx = serverData.Server.ServerStatus;

        Assert.AreEqual(serverId, server.ServerId);
        Assert.AreEqual(serverInfo1.Version.ToString(), server.Version);
        Assert.AreEqual(serverInfo1.EnvironmentVersion.ToString(), server.EnvironmentVersion ?? "0.0.0");
        Assert.AreEqual(serverInfo1.OsInfo, server.OsInfo);
        Assert.AreEqual(serverInfo1.MachineName, server.MachineName);
        Assert.AreEqual(serverInfo1.TotalMemory, server.TotalMemory);
        Assert.IsTrue(dateTime <= server.ConfigureTime);
        Assert.IsNotNull(serverStatusEx);

        Assert.AreEqual(serverInfo1.Status.FreeMemory, serverStatusEx.FreeMemory);
        Assert.AreEqual(ServerState.Configuring, server.ServerState);
        Assert.AreEqual(serverInfo1.Status.TcpConnectionCount, serverStatusEx.TcpConnectionCount);
        Assert.AreEqual(serverInfo1.Status.UdpConnectionCount, serverStatusEx.UdpConnectionCount);
        Assert.AreEqual(serverInfo1.Status.SessionCount, serverStatusEx.SessionCount);
        Assert.AreEqual(serverInfo1.Status.ThreadCount, serverStatusEx.ThreadCount);
        Assert.IsTrue(dateTime <= serverStatusEx.CreatedTime);

        //-----------
        // check: Check ServerStatus log is inserted
        //-----------
        var serverStatus = TestInit.NewServerStatus(serverConfig.ConfigCode);

        dateTime = DateTime.UtcNow;
        await Task.Delay(500);
        await agentClient1.Server_UpdateStatus(serverStatus);
        await TestInit1.Sync();
        await agentClient1.Server_UpdateStatus(serverStatus); // last status will not be synced
        await TestInit1.Sync();

        serverData = await serverClient.GetAsync(TestInit1.ProjectId, serverId);
        server = serverData.Server;
        Assert.AreEqual(serverStatus.FreeMemory, server.ServerStatus?.FreeMemory);
        Assert.AreNotEqual(ServerState.Configuring, server.ServerState);
        Assert.AreEqual(serverStatus.TcpConnectionCount, server.ServerStatus?.TcpConnectionCount);
        Assert.AreEqual(serverStatus.UdpConnectionCount, server.ServerStatus?.UdpConnectionCount);
        Assert.AreEqual(serverStatus.SessionCount, server.ServerStatus?.SessionCount);
        Assert.AreEqual(serverStatus.ThreadCount, server.ServerStatus?.ThreadCount);
        Assert.IsTrue(server.ServerStatus?.CreatedTime > dateTime);
    }

    [TestMethod]
    public async Task ServerStatus_recovery_by_cache()
    {
        var sampler = await SampleAccessPointGroup.Create(serverCount: 1);
        var server = await sampler.AddNewServer();
        
        // Clear Cache
        await sampler.TestInit.FlushCache();
        await sampler.TestInit.AgentCacheClient.InvalidateProject(sampler.ProjectId);

        // update status again
        await server.UpdateStatus(server.ServerInfo.Status);
        var servers = await sampler.TestInit.AgentCacheClient.GetServers(sampler.ProjectId);
        Assert.IsTrue(servers.Any(x=>x.ServerId==server.ServerId));
    }

    [TestMethod]
    public async Task Configure_reconfig()
    {
        var serverClient = new ServerClient(TestInit1.Http);
        var agentClient = TestInit1.CreateAgentClient();

        var serverId = TestInit1.ServerId1;
        var oldCode = TestInit1.ServerInfo1.Status.ConfigCode;

        //-----------
        // check
        //-----------
        await serverClient.UpdateAsync(TestInit1.ProjectId, serverId,
            new ServerUpdateParams
            { AccessPointGroupId = new PatchOfNullableGuid { Value = TestInit1.AccessPointGroupId2 } });
        await serverClient.UpdateAsync(TestInit1.ProjectId, serverId,
            new ServerUpdateParams { AccessPointGroupId = new PatchOfNullableGuid { Value = null } });
        var serverCommand = await TestInit1.AgentClient1.Server_UpdateStatus(new ServerStatus { ConfigCode = oldCode });
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "Updating AccessPointGroupId should lead to a new ConfigCode");
        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        var accessPointClient = new AccessPointClient(TestInit1.Http);
        var accessPoint = await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = serverId,
                IpAddress = await TestInit1.NewIpV4String(),
                AccessPointGroupId = TestInit1.AccessPointGroupId2,
                IsListen = true
            });
        serverCommand = await TestInit1.AgentClient1.Server_UpdateStatus(new ServerStatus { ConfigCode = oldCode });
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "add an AccessPoint should lead to a new ConfigCode.");
        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        await accessPointClient.UpdateAsync(TestInit1.ProjectId, accessPoint.AccessPointId,
            new AccessPointUpdateParams { IsListen = new PatchOfBoolean { Value = !accessPoint.IsListen } });
        var serverStatus = new ServerStatus { ConfigCode = oldCode };
        serverCommand = await TestInit1.AgentClient1.Server_UpdateStatus(serverStatus);
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "updating AccessPoint should lead to a new ConfigCode.");
        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        await agentClient.Server_Configure(await TestInit1.NewServerInfo());
        var serverModel = await TestInit1.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverId);
        Assert.AreEqual(serverStatus.ConfigCode, serverModel.LastConfigCode.ToString(),
            "LastConfigCode should be set by Server_UpdateStatus.");

        Assert.AreEqual(oldCode, serverModel.ConfigCode.ToString(),
            "ConfigCode should not be changed by ConfigureServer.");

        Assert.AreNotEqual(serverModel.LastConfigCode, serverModel.ConfigCode,
            "LastConfigCode should be changed after UpdateStatus.");

        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        serverStatus = new ServerStatus { ConfigCode = Guid.NewGuid().ToString() };
        await TestInit1.AgentClient1.Server_UpdateStatus(serverStatus);
        serverModel = await TestInit1.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverId);
        Assert.AreEqual(serverStatus.ConfigCode, serverModel.LastConfigCode.ToString(),
            "LastConfigCode should be changed even by incorrect ConfigCode");
        Assert.AreEqual(oldCode, serverModel.ConfigCode.ToString(),
            "ConfigCode should not be changed when there is no update");

        //-----------
        // check
        //-----------
        await TestInit1.AgentClient1.Server_UpdateStatus(new ServerStatus { ConfigCode = oldCode });
        serverModel = await TestInit1.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverId);
        Assert.AreEqual(serverModel.ConfigCode, serverModel.LastConfigCode,
            "LastConfigCode should be changed correct ConfigCode");

        //-----------
        // check Reconfig After Config finish
        //-----------
        await accessPointClient.UpdateAsync(TestInit1.ProjectId, accessPoint.AccessPointId,
            new AccessPointUpdateParams { UdpPort = new PatchOfInteger() { Value = 9090 } });
        var serverData = await TestInit1.ServerClient.GetAsync(TestInit1.ProjectId, serverId);
        Assert.AreEqual(ServerState.Configuring, serverData.Server.ServerState);
    }

    [TestMethod]
    public async Task Configure_on_auto_update_accessPoints()
    {
        // create serverInfo
        var accessPointGroupClient = new AccessPointGroupClient(TestInit1.Http);

        var accessPointGroup1 =
            await accessPointGroupClient.CreateAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        var serverClient = new ServerClient(TestInit1.Http);
        var server = await serverClient.CreateAsync(TestInit1.ProjectId,
            new ServerCreateParams { AccessPointGroupId = accessPointGroup1.AccessPointGroupId });

        var publicInTokenAccessPoint1 = await Configure_auto_update_accessPoints_on_internal(server);
        var publicInTokenAccessPoint2 = await Configure_auto_update_accessPoints_on_internal(server);

        // --------
        // Check: The only PublicInToken should be changed by second configure
        // --------
        Assert.IsNotNull(publicInTokenAccessPoint1);
        Assert.IsNotNull(publicInTokenAccessPoint2);
        Assert.AreNotEqual(publicInTokenAccessPoint1.IpAddress, publicInTokenAccessPoint2.IpAddress);

        // --------
        // Check: Keep last server tokenAccessPoint if publicIp is same
        // --------

        // create serverInfo
        var serverInfo = await TestInit1.NewServerInfo();
        serverInfo.PrivateIpAddresses = new[] { await TestInit1.NewIpV4(), await TestInit1.NewIpV6() };
        serverInfo.PublicIpAddresses = new[]
        {
            await TestInit1.NewIpV4(), await TestInit1.NewIpV6(), IPAddress.Parse(publicInTokenAccessPoint2.IpAddress)
        };

        //Configure
        var agentClient = TestInit1.CreateAgentClient(server.ServerId);
        await agentClient.Server_Configure(serverInfo);
        var accessPointClient = new AccessPointClient(TestInit1.Http);
        var accessPoints = await accessPointClient.ListAsync(TestInit1.ProjectId, server.ServerId);
        Assert.AreEqual(publicInTokenAccessPoint2.IpAddress,
            accessPoints.Single(x => x.AccessPointMode == AccessPointMode.PublicInToken).IpAddress);

        // --------
        // Check: another server with same group should not have any PublicInTokenAccess
        // --------
        server = await serverClient.CreateAsync(TestInit1.ProjectId,
            new ServerCreateParams { AccessPointGroupId = accessPointGroup1.AccessPointGroupId });
        var publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(server);
        Assert.IsNull(publicInTokenAccessPoint);

        // --------
        // Check: another server with different group should have one PublicInTokenAccess
        // --------
        var accessPointGroup2 =
            await accessPointGroupClient.CreateAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        server = await serverClient.CreateAsync(TestInit1.ProjectId,
            new ServerCreateParams { AccessPointGroupId = accessPointGroup2.AccessPointGroupId });
        publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(server);
        Assert.IsNotNull(publicInTokenAccessPoint);
    }

    // return the only PublicInToken AccessPoint
    public async Task<AccessPoint?> Configure_auto_update_accessPoints_on_internal(Api.Server2 server)
    {
        var accessPointClient = new AccessPointClient(TestInit1.Http);

        // create serverInfo
        var serverInfo = await TestInit1.NewServerInfo();
        var publicIp = await TestInit1.NewIpV6();
        var privateIp = await TestInit1.NewIpV4();
        serverInfo.PrivateIpAddresses = new[] { publicIp, privateIp, await TestInit1.NewIpV6(), privateIp };
        serverInfo.PublicIpAddresses = new[] { publicIp, await TestInit1.NewIpV4(), await TestInit1.NewIpV6() };

        //Configure
        var agentClient = TestInit1.CreateAgentClient(server.ServerId);
        var serverConfig = await agentClient.Server_Configure(serverInfo);
        Assert.AreEqual(TestInit1.AgentOptions.ServerUpdateStatusInterval, serverConfig.UpdateStatusInterval);
        Assert.AreEqual(serverConfig.TcpEndPoints.Length, serverConfig.TcpEndPoints.Distinct().Count(),
            "Duplicate listener!");

        //-----------
        // check: Configure with AutoUpdate is true (Server.AccessPointGroupId is set)
        //-----------
        var accessPoints = (await accessPointClient.ListAsync(TestInit1.ProjectId, server.ServerId)).ToArray();
        var totalServerInfoIpAddress =
            serverInfo.PrivateIpAddresses.Concat(serverInfo.PublicIpAddresses).Distinct().Count();
        Assert.AreEqual(totalServerInfoIpAddress, accessPoints.Length);

        // private[0]
        var accessPoint =
            accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[0].ToString());
        var accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken,
            "shared publicIp and privateIp must be see as publicIp");
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // private[1]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[1].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen);
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // private[2]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[2].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen);
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[0]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[0].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[1]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[1].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsFalse(accessPoint.IsListen);
        Assert.IsFalse(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[2]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[2].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsFalse(accessPoint.IsListen);
        Assert.IsFalse(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // PublicInToken should never be deleted
        return accessPoints.SingleOrDefault(x => x.AccessPointMode == AccessPointMode.PublicInToken);
    }

    [TestMethod]
    public async Task Configure_off_auto_update_accessPoints()
    {
        // create serverInfo
        var serverClient = new ServerClient(TestInit1.Http);
        var server =
            await serverClient.CreateAsync(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = null });

        var accessPointClient = new AccessPointClient(TestInit1.Http);
        var accessPoint1 = await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = server.ServerId,
                IpAddress = await TestInit1.NewIpV4String(),
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                AccessPointMode = AccessPointMode.PublicInToken,
                IsListen = true,
                TcpPort = 4848,
                UdpPort = 150
            });

        var accessPoint2 = await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = server.ServerId,
                IpAddress = await TestInit1.NewIpV4String(),
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                AccessPointMode = AccessPointMode.Private,
                IsListen = true,
                TcpPort = 5010,
                UdpPort = 0
            });

        var serverInfo1 = await TestInit1.NewServerInfo();
        var publicIp = await TestInit1.NewIpV6();
        serverInfo1.PrivateIpAddresses = new[] { publicIp, await TestInit1.NewIpV4(), await TestInit1.NewIpV6() };
        serverInfo1.PublicIpAddresses = new[] { publicIp, await TestInit1.NewIpV4(), await TestInit1.NewIpV6() };

        // Configure
        var agentClient1 = TestInit1.CreateAgentClient(server.ServerId);
        await agentClient1.Server_Configure(serverInfo1);

        // Test that accessPoints have not been changed
        var accessPoints = await accessPointClient.ListAsync(TestInit1.ProjectId, server.ServerId);
        Assert.AreEqual(2, accessPoints.Count);

        // AccessPoint1
        var expectedAccessPoint = accessPoint1;
        var actualAccessPoint = accessPoints.Single(x => x.IpAddress == expectedAccessPoint.IpAddress);
        Assert.AreEqual(expectedAccessPoint.AccessPointMode, actualAccessPoint.AccessPointMode);
        Assert.AreEqual(expectedAccessPoint.TcpPort, actualAccessPoint.TcpPort);
        Assert.AreEqual(expectedAccessPoint.UdpPort, actualAccessPoint.UdpPort);
        Assert.AreEqual(expectedAccessPoint.AccessPointGroupId, actualAccessPoint.AccessPointGroupId);
        Assert.AreEqual(expectedAccessPoint.IsListen, actualAccessPoint.IsListen);

        // AccessPoint2
        expectedAccessPoint = accessPoint2;
        actualAccessPoint = accessPoints.Single(x => x.IpAddress == expectedAccessPoint.IpAddress);
        Assert.AreEqual(expectedAccessPoint.AccessPointMode, actualAccessPoint.AccessPointMode);
        Assert.AreEqual(expectedAccessPoint.TcpPort, actualAccessPoint.TcpPort);
        Assert.AreEqual(expectedAccessPoint.UdpPort, actualAccessPoint.UdpPort);
        Assert.AreEqual(expectedAccessPoint.AccessPointGroupId, actualAccessPoint.AccessPointGroupId);
        Assert.AreEqual(expectedAccessPoint.IsListen, actualAccessPoint.IsListen);
    }

    class TestServer
    {
        public TestServer(TestInit testInit, Guid groupId, bool configure = true, bool sendStatus = true,
            IPEndPoint? serverEndPoint = null)
        {
            ServerEndPoint = serverEndPoint ?? testInit.NewEndPoint().Result;
            Server = testInit.ServerClient.CreateAsync(testInit.ProjectId, new ServerCreateParams()).Result;
            testInit.AccessPointClient.CreateAsync(testInit.ProjectId,
                new AccessPointCreateParams
                {
                    ServerId = Server.ServerId,
                    IpAddress = ServerEndPoint.Address.ToString(),
                    AccessPointGroupId = groupId,
                    AccessPointMode = AccessPointMode.Public,
                    TcpPort = ServerEndPoint.Port,
                    IsListen = true
                }).Wait();
            AgentClient = testInit.CreateAgentClient(Server.ServerId);
            ServerStatus.SessionCount = 0;

            if (configure)
            {
                var serverInfo = testInit.NewServerInfo().Result;
                serverInfo.Status = ServerStatus;

                var config = AgentClient.Server_Configure(serverInfo).Result;
                if (sendStatus)
                {
                    ServerStatus.ConfigCode = config.ConfigCode;
                    AgentClient.Server_UpdateStatus(serverInfo.Status).Wait();
                }
            }
        }

        public IPEndPoint ServerEndPoint { get; }
        public Server2 Server { get; }
        public AgentClient AgentClient { get; }
        public ServerStatus ServerStatus { get; } = TestInit.NewServerStatus(null);
    }

    [TestMethod]
    public async Task LoadBalancer()
    {
        var testInit = await TestInit.Create(false);
        testInit.AgentOptions.AllowRedirect = true;
        var accessPointGroup = await testInit.AccessPointGroupClient.CreateAsync(testInit.ProjectId, new AccessPointGroupCreateParams());

        // Create and init servers
        var testServers = new List<TestServer>();
        for (var i = 0; i < 4; i++)
        {
            var testServer = new TestServer(testInit, accessPointGroup.AccessPointGroupId, i != 3);
            testServers.Add(testServer);
        }

        testServers.Add(new TestServer(testInit, accessPointGroup.AccessPointGroupId, true, true,
            await testInit.NewEndPointIp6()));
        testServers.Add(new TestServer(testInit, accessPointGroup.AccessPointGroupId, true, false));

        // create access token
        var accessToken = await testInit.AccessTokenClient.CreateAsync(testInit.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = accessPointGroup.AccessPointGroupId,
            });

        // create sessions
        var agentClient = testInit.CreateAgentClient(testServers[0].Server.ServerId);
        for (var i = 0; i < 9; i++)
        {
            var testServer = testServers[0];
            var sessionRequestEx =
                testInit.CreateSessionRequestEx(accessToken, hostEndPoint: testServer.ServerEndPoint);
            var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
            if (sessionResponseEx.ErrorCode == SessionErrorCode.RedirectHost)
            {
                Assert.IsNotNull(sessionResponseEx.RedirectHostEndPoint);
                sessionRequestEx.HostEndPoint = sessionResponseEx.RedirectHostEndPoint;
                testServer = testServers.First(x =>
                    sessionResponseEx.RedirectHostEndPoint.Equals(x.ServerEndPoint));
                sessionResponseEx = await testServer.AgentClient.Session_Create(sessionRequestEx);
            }

            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode, sessionResponseEx.ErrorMessage);
            testServer.ServerStatus.SessionCount++;
            await testServer.AgentClient.Server_UpdateStatus(testServer.ServerStatus);
        }

        // some server should not be selected
        Assert.AreEqual(0, testServers[3].ServerStatus.SessionCount, "A server with configuring state is selected.");
        Assert.AreEqual(0, testServers[4].ServerStatus.SessionCount, "IpVersion is not respected.");
        Assert.AreEqual(0, testServers[5].ServerStatus.SessionCount, "Should not use server in Configuring state.");

        // each server sessions must be 3
        Assert.AreEqual(3, testServers[0].ServerStatus.SessionCount);
        Assert.AreEqual(3, testServers[1].ServerStatus.SessionCount);
        Assert.AreEqual(3, testServers[2].ServerStatus.SessionCount);
    }

    [TestMethod]
    public async Task Fail_Configure_by_old_version()
    {
        // create serverInfo
        var serverClient = new ServerClient(TestInit1.Http);
        var server = await serverClient.CreateAsync(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });

        // create serverInfo
        var agentClient1 = TestInit1.CreateAgentClient(server.ServerId);
        var serverInfo1 = await TestInit1.NewServerInfo();
        var publicIp = await TestInit1.NewIpV6();
        serverInfo1.PrivateIpAddresses = new[] { publicIp, (await TestInit1.NewIpV4()), (await TestInit1.NewIpV6()) };
        serverInfo1.PublicIpAddresses = new[] { publicIp, (await TestInit1.NewIpV4()), (await TestInit1.NewIpV6()) };

        //Configure
        serverInfo1.Version = Version.Parse("0.0.1");
        try
        {
            await agentClient1.Server_Configure(serverInfo1);
        }
        catch (ApiException e)
        {
            var serverData = await serverClient.GetAsync(TestInit1.ProjectId, server.ServerId);
            Assert.AreEqual(nameof(NotSupportedException), e.ExceptionTypeName);
            Assert.IsTrue(serverData.Server.LastConfigError?.Contains("version", StringComparison.OrdinalIgnoreCase));
            serverInfo1.LastError = serverData.Server.LastConfigError;
        }

        // LastConfigError must be removed after successful configuration
        serverInfo1.Version = ServerUtils.ServerUtil.MinServerVersion;
        var configure = await agentClient1.Server_Configure(serverInfo1);
        await agentClient1.Server_UpdateStatus(TestInit.NewServerStatus(configure.ConfigCode));
        var serverData2 = await serverClient.GetAsync(TestInit1.ProjectId, server.ServerId);
        Assert.IsNull(serverData2.Server.LastConfigError);
    }

    [TestMethod]
    public async Task Session_Create_Status_SuppressToOther()
    {
        var sampler = await SampleAccessPointGroup.Create();
        var accessToken = await sampler.TestInit.AccessTokenClient.CreateAsync(sampler.ProjectId, new AccessTokenCreateParams
        {
            AccessPointGroupId = sampler.AccessPointGroupId,
            MaxDevice = 2
        });

        var sampleAccessToken = new SampleAccessToken(sampler.TestInit, accessToken);
        var sampleSession1 = await sampleAccessToken.CreateSession();
        await sampleAccessToken.CreateSession();
            
        var sampleSession = await sampleAccessToken.CreateSession();
        Assert.AreEqual(SessionSuppressType.Other, sampleSession.SessionResponseEx.SuppressedTo);

        var res = await sampleSession1.AddUsage(0);
        Assert.AreEqual(SessionSuppressType.Other, res.SuppressedBy);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, res.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_Status_SuppressToYourself()
    {
        var sampler = await SampleAccessPointGroup.Create();
        var accessToken = await sampler.TestInit.AccessTokenClient.CreateAsync(sampler.ProjectId, new AccessTokenCreateParams
        {
            AccessPointGroupId = sampler.AccessPointGroupId,
            MaxDevice = 2
        });

        var sampleAccessToken = new SampleAccessToken(sampler.TestInit, accessToken);
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