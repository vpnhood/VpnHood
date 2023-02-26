using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Services;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;

namespace VpnHood.AccessServer.Test.Tests;


[TestClass]
public class AgentClientSessionTest
{
    [TestMethod]
    public async Task Session_Create_Status_TrafficOverflow()
    {
        var farm = await AccessPointGroupDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(new AccessTokenCreateParams
        {
            MaxTraffic = 14
        });

        //-----------
        // check: add usage
        //-----------
        var sessionDom = await accessTokenDom.CreateSession();
        var sessionResponse = await sessionDom.AddUsage(5, 10);
        await farm.TestInit.FlushCache();
        Assert.AreEqual(5, sessionResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, sessionResponse.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.AccessTrafficOverflow, sessionResponse.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_Status_No_TrafficOverflow_when_maxTraffic_is_zero()
    {
        var farm = await AccessPointGroupDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(new AccessTokenCreateParams
        {
            MaxTraffic = 0
        });

        //-----------
        // check: add usage
        //-----------
        var sessionDom = await accessTokenDom.CreateSession();
        var sessionResponse = await sessionDom.AddUsage(5, 10);
        Assert.AreEqual(5, sessionResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, sessionResponse.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponse.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_success()
    {
        var farm = await AccessPointGroupDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(new AccessTokenCreateParams
        {
            MaxTraffic = 100,
            ExpirationTime = new DateTime(2040, 1, 1),
            Lifetime = 0,
            MaxDevice = 22
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
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.ReceivedTraffic);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.SentTraffic);
        Assert.IsNotNull(sessionResponseEx.SessionKey);
        Assert.IsTrue(accessTokenData.Access!.CreatedTime >= beforeUpdateTime);
        Assert.IsTrue(accessTokenData.Access!.CreatedTime >= beforeUpdateTime);

        // check Device id and its properties are created 
        var clientInfo = sessionDom.SessionRequestEx.ClientInfo;
        var device = await farm.TestInit.DevicesClient.FindByClientIdAsync(farm.TestInit.ProjectId, clientInfo.ClientId);
        Assert.AreEqual(clientInfo.ClientId, device.ClientId);
        Assert.AreEqual(clientInfo.UserAgent, device.UserAgent);
        Assert.AreEqual(clientInfo.ClientVersion, device.ClientVersion);

        // check updating same client
        var sessionRequestEx = sessionDom.SessionRequestEx;
        sessionRequestEx.ClientIp = await farm.TestInit.NewIpV4();
        sessionRequestEx.ClientInfo.UserAgent = "userAgent2";
        sessionRequestEx.ClientInfo.ClientVersion = "200.0.0";
        await farm.DefaultServer.AgentClient.Session_Create(sessionRequestEx);
        device = await farm.TestInit.DevicesClient.FindByClientIdAsync(farm.TestInit.ProjectId, clientInfo.ClientId);
        Assert.AreEqual(sessionRequestEx.ClientInfo.UserAgent, device.UserAgent);
        Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, device.ClientVersion);
    }

    private async Task<Models.AccessModel> GetAccessFromSession(SessionDom sessionDom)
    {
        await using var scope =  sessionDom.TestInit.WebApp.Services.CreateAsyncScope();
        var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        var session = await vhContext.Sessions
            .Include(x => x.Access)
            .SingleAsync(x => x.SessionId == sessionDom.SessionId);

        return session.Access!;
    }

    [TestMethod]
    public async Task Session_Get()
    {
        var accessPointGroupDom = await AccessPointGroupDom.Create();
        var accessTokenDom = await accessPointGroupDom.CreateAccessToken();

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
        var accessTokenDom = await accessPointGroupDom.CreateAccessToken();

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
        var accessTokenDom = await accessPointGroupDom.CreateAccessToken();

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
        var farm1 = await AccessPointGroupDom.Create();
        var serverDom11 = await farm1.AddNewServer();
        var serverDom12 = await farm1.AddNewServer();
        var accessTokenDom11 = await farm1.CreateAccessToken(true);

        var farm2 = await AccessPointGroupDom.Create();
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

        //-----------
        // check: access should not grant by another farm token
        //-----------
        try
        {
            await serverDom21.CreateSession(accessTokenDom11.AccessToken);
            Assert.Fail("NotExistsException was Expected");
        }
        catch (ApiException e)
        {
            Assert.AreEqual(nameof(NotExistsException), e.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Session_Close()
    {
        var testInit = await TestInit.Create();
        testInit.AgentOptions.SessionPermanentlyTimeout = TimeSpan.FromSeconds(2);
        var sampleFarm1 = await SampleFarm.Create(testInit);
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
        Assert.AreEqual(responseBase.AccessUsage!.SentTraffic + 10, responseBase2.AccessUsage!.SentTraffic,
            "AddUsage must work on closed a session!");
        Assert.AreEqual(responseBase.AccessUsage!.ReceivedTraffic + 5, responseBase2.AccessUsage!.ReceivedTraffic,
            "AddUsage must work on closed a session!");

        //-----------
        // check: The Session should not exist after sync
        //-----------
        await Task.Delay(testInit.AgentOptions.SessionPermanentlyTimeout);
        await testInit.Sync();
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
        var testInit = await TestInit.Create();
        var sampleFarm1 = await SampleFarm.Create(testInit);
        var sampleFarm2 = await SampleFarm.Create(testInit);

        var createSessionTasks = new List<Task<SessionDom>>();
        for (var i = 0; i < 50; i++)
        {
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
        await testInit.Sync();

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

        await testInit.FlushCache();
        tasks = sampleFarm1.Server1.Sessions.Select(x => x.AddUsage());
        tasks = tasks.Concat(sampleFarm1.Server2.Sessions.Select(x => x.AddUsage()));
        tasks = tasks.Concat(sampleFarm2.Server1.Sessions.Select(x => x.AddUsage()));
        tasks = tasks.Concat(sampleFarm2.Server2.Sessions.Select(x => x.AddUsage()));
        await Task.WhenAll(tasks);


        await testInit.Sync();
    }

    [TestMethod]
    public async Task Session_AddUsage_Public()
    {
        var farm = await AccessPointGroupDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true);
        var sessionDom1 = await accessTokenDom.CreateSession();

        //--------------
        // check: zero usage
        //--------------
        var response = await sessionDom1.AddUsage(0);
        Assert.AreEqual(0, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(0, response.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        var access = await GetAccessFromSession(sessionDom1);
        Assert.AreEqual(0, access.TotalSentTraffic);
        Assert.AreEqual(0, access.TotalReceivedTraffic);
        Assert.AreEqual(0, access.TotalTraffic);

        //-----------
        // check: add usage
        //-----------
        response = await sessionDom1.AddUsage(5, 10);
        await farm.TestInit.FlushCache();
        Assert.AreEqual(5, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, response.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        access = await GetAccessFromSession(sessionDom1);
        Assert.AreEqual(5, access.TotalSentTraffic);
        Assert.AreEqual(10, access.TotalReceivedTraffic);
        Assert.AreEqual(15, access.TotalTraffic);

        // again
        response = await sessionDom1.AddUsage(5, 10);
        await farm.TestInit.FlushCache();
        Assert.AreEqual(10, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(20, response.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        access = await GetAccessFromSession(sessionDom1);
        Assert.AreEqual(10, access.TotalSentTraffic);
        Assert.AreEqual(20, access.TotalReceivedTraffic);
        Assert.AreEqual(30, access.TotalTraffic);

        //-----------
        // check: add usage for client 2
        //-----------
        var sessionDom2 = await accessTokenDom.CreateSession();
        response = await sessionDom2.AddUsage(5,10);
        await farm.TestInit.FlushCache();

        Assert.AreEqual(5, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, response.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        access = await GetAccessFromSession(sessionDom2);
        Assert.AreEqual(5, access.TotalSentTraffic);
        Assert.AreEqual(10, access.TotalReceivedTraffic);
        Assert.AreEqual(15, access.TotalTraffic);

        //-------------
        // check: add usage to client 1 after cycle
        //-------------

        //remove last cycle
        var cycleManager = farm.TestInit.Scope.ServiceProvider.GetRequiredService<UsageCycleService>();
        await cycleManager.DeleteCycle(cycleManager.CurrentCycleId);
        await cycleManager.UpdateCycle();

        response = await sessionDom2.AddUsage(5, 10);
        Assert.AreEqual(5, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, response.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        //-------------
        // check: another Session_Create for same client should return same result
        //-------------
        var sessionDom3 = await accessTokenDom.CreateSession(clientId: sessionDom2.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(5, sessionDom3.SessionResponseEx.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, sessionDom3.SessionResponseEx.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom3.SessionResponseEx.ErrorCode);

        //-------------
        // check: Session for another client should be reset too
        //-------------
        response = await sessionDom1.AddUsage(50, 100);
        await farm.TestInit.FlushCache();
        Assert.AreEqual(50, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(100, response.AccessUsage?.ReceivedTraffic);
    }

    [TestMethod]
    public async Task Session_AddUsage_Private()
    {
        var farm = await AccessPointGroupDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(isPublic: false);
        var sessionDom1 = await accessTokenDom.CreateSession();

        //--------------
        // check: zero usage
        //--------------
        var response = await sessionDom1.AddUsage(0);
        Assert.AreEqual(0, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(0, response.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        //-----------
        // check: add usage by client 1
        //-----------
        response = await sessionDom1.AddUsage(5, 10);
        await farm.TestInit.FlushCache();
        Assert.AreEqual(5, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, response.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);

        var accessData = await accessTokenDom.Reload();
        Assert.AreEqual(5, accessData.Access?.TotalSentTraffic);
        Assert.AreEqual(10, accessData.Access?.TotalReceivedTraffic);

        //-----------
        // check: add usage by client 2
        //-----------
        var sessionDom2 = await accessTokenDom.CreateSession();
        var response2 = await sessionDom2.AddUsage(5, 10);
        Assert.AreEqual(10, response2.AccessUsage?.SentTraffic);
        Assert.AreEqual(20, response2.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response2.ErrorCode);

        await farm.TestInit.FlushCache();
        accessData = await accessTokenDom.Reload();
        Assert.AreEqual(10, accessData.Access?.TotalSentTraffic);
        Assert.AreEqual(20, accessData.Access?.TotalReceivedTraffic);
    }

    [TestMethod]
    public async Task AccessUsage_Inserted()
    {
        var farm = await AccessPointGroupDom.Create();

        // create token
        var sampleAccessToken = await farm.CreateAccessToken();
        var sessionDom = await sampleAccessToken.CreateSession();
        await sessionDom.AddUsage(10051, 20051);
        await sessionDom.AddUsage(20, 30);
        await sessionDom.CloseSession();

        await farm.TestInit.FlushCache();

        var session = await farm.TestInit.VhContext.Sessions
            .Include(x => x.Access)
            .Include(x => x.Access!.AccessToken)
            .SingleAsync(x => x.SessionId == sessionDom.SessionId);

        var deviceData = await farm.TestInit.DevicesClient.GetAsync(farm.ProjectId, session.DeviceId);

        Assert.AreEqual(sampleAccessToken.AccessTokenId, session.Access?.AccessTokenId);
        Assert.AreEqual(sessionDom.SessionRequestEx.ClientInfo.ClientId, deviceData.Device.ClientId);
        Assert.AreEqual(IPAddressUtil.Anonymize(sessionDom.SessionRequestEx.ClientIp!).ToString(), session.DeviceIp);
        Assert.AreEqual(sessionDom.SessionRequestEx.ClientInfo.ClientVersion, session.ClientVersion);

        // check sync
        await farm.TestInit.Sync();

        var accessUsage = await farm.TestInit.VhReportContext.AccessUsages
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

        var sessionDom = await sampleAccessToken.CreateSession(clientId);
        Assert.AreEqual(SessionSuppressType.YourSelf, sessionDom.SessionResponseEx.SuppressedTo);

        var res = await sampleSession1.AddUsage(0);
        Assert.AreEqual(SessionSuppressType.YourSelf, res.SuppressedBy);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, res.ErrorCode);
    }
}