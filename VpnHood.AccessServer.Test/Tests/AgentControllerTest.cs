using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Net;
using AccessPointMode = VpnHood.AccessServer.Api.AccessPointMode;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AgentControllerTest : ControllerTest
{
    [TestMethod]
    public async Task Session_Create_Status_Expired()
    {
        var accessTokenController = new AccessTokenController(TestInit1.Http);

        // create accessToken
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                EndTime = new DateTime(1900, 1, 1)
            });
        var agentController = TestInit1.CreateAgentController();

        var sessionResponseEx = await agentController.SessionsPostAsync(TestInit1.CreateSessionRequestEx(accessToken));
        Assert.AreEqual(SessionErrorCode.AccessExpired, sessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Create_Status_TrafficOverflow()
    {
        var accessTokenController = new AccessTokenController(TestInit1.Http);

        // create accessToken
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                MaxTraffic = 14
            });

        // get access
        var agentController = TestInit1.CreateAgentController();
        var sessionResponseEx = await agentController.SessionsPostAsync(TestInit1.CreateSessionRequestEx(accessToken));

        //-----------
        // check: add usage
        //-----------
        var sessionResponse = await agentController.UsageAsync(sessionResponseEx.SessionId, false,
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
        var accessTokenController = new AccessTokenController(TestInit1.Http);

        // create accessToken
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                MaxTraffic = 0
            });
        var agentController = TestInit1.CreateAgentController();

        //-----------
        // check: add usage
        //-----------
        var sessionResponseEx =
            await agentController.SessionsPostAsync(TestInit1.CreateSessionRequestEx(accessToken));
        var sessionResponse = await agentController.UsageAsync(sessionResponseEx.SessionId, false, new UsageInfo
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
        var accessTokenController = new AccessTokenController(TestInit1.Http);

        // create token
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                EndTime = null,
                Lifetime = 30
            });
        var agentController = TestInit1.CreateAgentController();

        //-----------
        // check: add usage
        //-----------
        var sessionResponseEx =
            await agentController.SessionsPostAsync(TestInit1.CreateSessionRequestEx(accessToken));
        Assert.IsNotNull(sessionResponseEx.AccessUsage);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.SentTraffic);
        Assert.AreEqual(0, sessionResponseEx.AccessUsage.ReceivedTraffic);
        Assert.IsNotNull(sessionResponseEx.AccessUsage.ExpirationTime);
        Assert.IsTrue((sessionResponseEx.AccessUsage.ExpirationTime.Value - DateTime.UtcNow.AddDays(30)).TotalSeconds < 10);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task Session_Get_should_update_accessedTime()
    {
        // create a session for token
        var agentController = TestInit1.CreateAgentController();
        var sessionRequestEx = TestInit1.CreateSessionRequestEx(TestInit1.AccessToken1);
        var sessionResponseEx = await agentController.SessionsPostAsync(sessionRequestEx);

        // get the token again
        var time = DateTime.UtcNow;
        await Task.Delay(100);
        var sessionResponseEx2 = await agentController.SessionsGetAsync(sessionResponseEx.SessionId, sessionRequestEx.HostEndPoint);
        Assert.AreEqual(sessionResponseEx.ErrorCode, sessionResponseEx2.ErrorCode);
        Assert.AreEqual(sessionResponseEx.SessionId, sessionResponseEx2.SessionId);
        CollectionAssert.AreEqual(sessionResponseEx.SessionKey, sessionResponseEx2.SessionKey);

        // ------
        // Check Access Time is modified
        // ------
        var sessionController = new SessionController(TestInit1.Http);
        var session = await sessionController.SessionsAsync(TestInit1.ProjectId, sessionResponseEx.SessionId);
        Assert.IsTrue(session.AccessedTime > time);
    }

    [TestMethod]
    public async Task Session_Create_should_not_reset_expiration_Time()
    {
        var expectedExpirationTime = DateTime.UtcNow.AddDays(10).Date;

        // create token
        var accessTokenController = new AccessTokenController(TestInit1.Http);
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId2,
                EndTime = expectedExpirationTime,
                Lifetime = 30
            });

        var agentController = TestInit1.CreateAgentController();
        var sessionResponseEx = await agentController.SessionsPostAsync(TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPointG2S1));
        Assert.AreEqual(expectedExpirationTime, sessionResponseEx.AccessUsage?.ExpirationTime);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task SessionsPostAsync()
    {
        // create token
        var accessTokenController = new AccessTokenController(TestInit1.Http);
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
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

        var agentController = TestInit1.CreateAgentController();
        var sessionResponseEx = await agentController.SessionsPostAsync(sessionRequestEx);
        var accessTokenData = await accessTokenController.AccessTokensGetAsync(TestInit1.ProjectId, sessionRequestEx.TokenId);

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
        var deviceController = new DeviceController(TestInit1.Http);
        var device = await deviceController.FindByClientAsync(TestInit1.ProjectId, clientInfo.ClientId);
        Assert.AreEqual(clientInfo.ClientId, device.ClientId);
        Assert.AreEqual(clientInfo.UserAgent, device.UserAgent);
        Assert.AreEqual(clientInfo.ClientVersion, device.ClientVersion);

        // check updating same client
        beforeUpdateTime = DateTime.UtcNow.AddSeconds(-1);
        sessionRequestEx.ClientIp = TestInit1.ClientIp2.ToString();
        sessionRequestEx.ClientInfo.UserAgent = "userAgent2";
        sessionRequestEx.ClientInfo.ClientVersion = "200.0.0";
        await agentController.SessionsPostAsync(sessionRequestEx);
        device = await deviceController.FindByClientAsync(TestInit1.ProjectId, sessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(sessionRequestEx.ClientInfo.UserAgent, device.UserAgent);
        Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, device.ClientVersion);

        // prepare report database
        await TestInit1.SyncToReport();

        accessTokenData = await accessTokenController.AccessTokensGetAsync(TestInit1.ProjectId, sessionRequestEx.TokenId, TestInit1.CreatedTime.AddSeconds(-1));
        Assert.IsTrue(accessTokenData.Access?.CreatedTime >= beforeUpdateTime);
        Assert.AreEqual(1, accessTokenData.Usage?.AccessTokenCount);
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
        var accessTokenController = new AccessTokenController(TestInit1.Http);

        // create first public token
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });

        var agentController = TestInit1.CreateAgentController(TestInit1.ServerId2);

        //-----------
        // check: access should grant to public token 1 by another public endpoint
        //-----------
        var sessionResponseEx = await agentController.SessionsPostAsync(TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPointG1S2));
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);

        //-----------
        // check: access should not grant to public token 1 by private server endpoint
        //-----------
        sessionResponseEx = await agentController.SessionsPostAsync(TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPointG2S1));
        Assert.AreEqual(SessionErrorCode.GeneralError, sessionResponseEx.ErrorCode);
        Assert.IsTrue(sessionResponseEx.ErrorMessage.Contains("Invalid EndPoint", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Session_AddUsage_Public()
    {
        // create token
        var accessTokenController = new AccessTokenController(TestInit1.Http);
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1, IsPublic = true });

        var agentController = TestInit1.CreateAgentController(TestInit1.ServerId1);
        var sessionRequestEx1 = TestInit1.CreateSessionRequestEx(accessToken);
        var sessionResponseEx1 = await agentController.SessionsPostAsync(sessionRequestEx1);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode);

        //--------------
        // check: zero usage
        //--------------
        var baseResponse = await agentController.UsageAsync(
            sessionResponseEx1.SessionId, false, new UsageInfo
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
        baseResponse = await agentController.UsageAsync(sessionResponseEx1.SessionId, false,
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
        baseResponse = await agentController.UsageAsync(sessionResponseEx1.SessionId, false,
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
        var sessionResponseEx2 = await agentController.SessionsPostAsync(sessionRequestEx2);
        baseResponse = await agentController.UsageAsync(sessionResponseEx2.SessionId, false,
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
        var cycleManager = TestInit1.WebApp.Services.GetRequiredService<UsageCycleManager>();
        await cycleManager.DeleteCycle(cycleManager.CurrentCycleId);
        await cycleManager.UpdateCycle();

        baseResponse = await agentController.UsageAsync(sessionResponseEx2.SessionId, false,
            new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
        await TestInit1.FlushCache();
        Assert.AreEqual(5, baseResponse.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, baseResponse.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

        //-------------
        // check: SessionsPostAsync for another client should return same result
        //-------------
        sessionResponseEx2 = await agentController.SessionsPostAsync(sessionRequestEx2);
        Assert.AreEqual(5, sessionResponseEx2.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, sessionResponseEx2.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx2.ErrorCode);


        //-------------
        // check: Session for another client should be reset too
        //-------------
        baseResponse = await agentController.UsageAsync(sessionResponseEx1.SessionId, false,
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
        var accessTokenController = new AccessTokenController(TestInit1.Http);

        // create token
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1, IsPublic = false });

        var agentController = TestInit1.CreateAgentController();
        var sessionRequestEx1 = TestInit1.CreateSessionRequestEx(accessToken);
        var sessionResponseEx1 = await agentController.SessionsPostAsync(sessionRequestEx1);

        //--------------
        // check: zero usage
        //--------------
        var response = await agentController.UsageAsync(sessionResponseEx1.SessionId, false,
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
        response = await agentController.UsageAsync(sessionResponseEx1.SessionId, false,
            new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
        await TestInit1.FlushCache();
        Assert.AreEqual(5, response.AccessUsage?.SentTraffic);
        Assert.AreEqual(10, response.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode);

        var accessData = await accessTokenController.AccessTokensGetAsync(TestInit1.ProjectId, accessToken.AccessTokenId);
        Assert.AreEqual(5, accessData.Access?.TotalSentTraffic);
        Assert.AreEqual(10, accessData.Access?.TotalReceivedTraffic);

        // again by client 2
        var sessionRequestEx2 = TestInit1.CreateSessionRequestEx(accessToken);
        var sessionResponseEx2 = await agentController.SessionsPostAsync(sessionRequestEx2);
        var response2 = await agentController.UsageAsync(sessionResponseEx2.SessionId, false,
            new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
        await TestInit1.FlushCache();
        Assert.AreEqual(10, response2.AccessUsage?.SentTraffic);
        Assert.AreEqual(20, response2.AccessUsage?.ReceivedTraffic);
        Assert.AreEqual(SessionErrorCode.Ok, response2.ErrorCode);

        accessData = await accessTokenController.AccessTokensGetAsync(TestInit1.ProjectId, accessToken.AccessTokenId);
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
        var accessPointController = new AccessPointController(TestInit1.Http);
        await accessPointController.AccessPointsPostAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1,
                IpAddress = publicEp1.Address.ToString(), 
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                TcpPort = publicEp1.Port,
                AccessPointMode = AccessPointMode.Public,
                IsListen = true
            });

        await accessPointController.AccessPointsPostAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1, 
                IpAddress = publicEp2.Address.ToString(), 
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                TcpPort = publicEp2.Port,
                AccessPointMode = AccessPointMode.PublicInToken,
                IsListen = false
            });

        await accessPointController.AccessPointsPostAsync(TestInit1.ProjectId,
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
        var agentController = TestInit1.CreateAgentController();
        var certBuffer = await agentController.CertificatesAsync(publicEp1.ToString());
        var certificate = new X509Certificate2(certBuffer);
        Assert.AreEqual(TestInit1.PublicServerDns, certificate.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // check: get certificate by privateIp
        //-----------
        certBuffer = await agentController.CertificatesAsync(privateEp.ToString());
        certificate = new X509Certificate2(certBuffer);
        Assert.AreEqual(TestInit1.PublicServerDns, certificate.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // check: check not found
        //-----------
        try
        {
            await agentController.CertificatesAsync(publicEp2.ToString());
            Assert.Fail("NotExistsException expected!");
        }
        catch (ApiException ex) when (ex.IsNotExistsException)
        {
        }
    }

    [TestMethod]
    public async Task StatusAsync()
    {
        var agentController1 = TestInit1.CreateAgentController(TestInit1.ServerId1);
        await agentController1.StatusAsync(new ServerStatus { SessionCount = 10 });

        var agentController2 = TestInit1.CreateAgentController(TestInit1.ServerId2);
        await agentController2.StatusAsync(new ServerStatus { SessionCount = 20 });

        var serverController = new ServerController(TestInit1.Http);

        var serverData1 = await serverController.ServersGetAsync(TestInit1.ProjectId, TestInit1.ServerId1);
        Assert.AreEqual(serverData1.Status?.SessionCount, 10);

        var serverData2 = await serverController.ServersGetAsync(TestInit1.ProjectId, TestInit1.ServerId2);
        Assert.AreEqual(serverData2.Status?.SessionCount, 20);
    }

    [TestMethod]
    public async Task AccessUsage_Inserted()
    {
        var accessTokenController = new AccessTokenController(TestInit1.Http);
        var agentController = TestInit1.CreateAgentController();

        // create token
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1, IsPublic = false });
        var sessionRequestEx = TestInit1.CreateSessionRequestEx(accessToken);
        sessionRequestEx.ClientInfo.UserAgent = "userAgent1";
        var sessionResponseEx = await agentController.SessionsPostAsync(sessionRequestEx);

        //-----------
        // check: add usage
        //-----------
        await agentController.UsageAsync(sessionResponseEx.SessionId, false,
            new UsageInfo { SentTraffic = 10051, ReceivedTraffic = 20051 });
        await agentController.UsageAsync(sessionResponseEx.SessionId, false,
            new UsageInfo { SentTraffic = 20, ReceivedTraffic = 30 });
        await TestInit1.FlushCache();

        await using var vhReportContext = TestInit1.Scope.ServiceProvider.GetRequiredService<VhReportContext>();
        await TestInit1.SyncToReport();

        var accessUsage = await vhReportContext.AccessUsages
            .OrderByDescending(x => x.AccessUsageId)
            .FirstAsync(x => x.SessionId == sessionResponseEx.SessionId);
        Assert.IsNotNull(accessUsage);

        await using var vhContext = TestInit1.Scope.ServiceProvider.GetRequiredService<VhContext>();
        var session = await vhContext.Sessions
            .Include(x => x.Access)
            .Include(x => x.Access!.AccessToken)
            .SingleAsync(x => x.SessionId == sessionResponseEx.SessionId);

        var deviceController = new DeviceController(TestInit1.Http);
        var deviceData = await deviceController.DevicesGetAsync(TestInit1.ProjectId, session.DeviceId);

        Assert.AreEqual(accessToken.AccessTokenId, session.Access?.AccessTokenId);
        Assert.AreEqual(sessionRequestEx.ClientInfo.ClientId, deviceData.Device.ClientId);
        Assert.AreEqual(IPAddressUtil.Anonymize(IPAddress.Parse(sessionRequestEx.ClientIp)).ToString(), session.DeviceIp);
        Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, session.ClientVersion);
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
        var serverController = new ServerController(TestInit1.Http);
        var serverId = (await serverController.ServersPostAsync(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 })).ServerId;
        var dateTime = DateTime.UtcNow.AddSeconds(-1);

        // create serverInfo
        var agentController1 = TestInit1.CreateAgentController(serverId);
        var serverInfo1 = await TestInit1.NewServerInfo();
        var publicIp = await TestInit1.NewIpV6();
        serverInfo1.PrivateIpAddresses = new[] { publicIp.ToString(), (await TestInit1.NewIpV4()).ToString(), (await TestInit1.NewIpV6()).ToString() };
        serverInfo1.PublicIpAddresses = new[] { publicIp.ToString(), (await TestInit1.NewIpV4()).ToString(), (await TestInit1.NewIpV6()).ToString() };

        //Configure
        await agentController1.ConfigureAsync(serverInfo1);
        await agentController1.ConfigureAsync(serverInfo1); // last status will not be synced
        await TestInit1.SyncToReport();

        var serverData = await serverController.ServersGetAsync(TestInit1.ProjectId, serverId);
        var server = serverData.Server;
        var serverStatusEx = serverData.Status;

        Assert.AreEqual(serverId, server.ServerId);
        Assert.AreEqual(serverInfo1.Version, server.Version);
        Assert.AreEqual(serverInfo1.EnvironmentVersion, server.EnvironmentVersion ?? "0.0.0");
        Assert.AreEqual(serverInfo1.OsInfo, server.OsInfo);
        Assert.AreEqual(serverInfo1.MachineName, server.MachineName);
        Assert.AreEqual(serverInfo1.TotalMemory, server.TotalMemory);
        Assert.IsTrue(dateTime <= server.ConfigureTime);
        Assert.IsNotNull(serverStatusEx);

        Assert.AreEqual(server.ServerId, serverStatusEx.ServerId);
        Assert.AreEqual(serverInfo1.Status.FreeMemory, serverStatusEx.FreeMemory);
        Assert.IsTrue(serverStatusEx.IsConfigure);
        Assert.AreEqual(serverInfo1.Status.TcpConnectionCount, serverStatusEx.TcpConnectionCount);
        Assert.AreEqual(serverInfo1.Status.UdpConnectionCount, serverStatusEx.UdpConnectionCount);
        Assert.AreEqual(serverInfo1.Status.SessionCount, serverStatusEx.SessionCount);
        Assert.AreEqual(serverInfo1.Status.ThreadCount, serverStatusEx.ThreadCount);
        Assert.IsTrue(serverStatusEx.IsLast);
        Assert.IsTrue(dateTime <= serverStatusEx.CreatedTime);

        //-----------
        // check: ConfigureLog is inserted
        //-----------
        var statusLogs = await serverController.StatusLogsAsync(TestInit1.ProjectId, server.ServerId, recordCount: 100);
        var statusLog = statusLogs.First();

        // check with serverData
        Assert.AreEqual(serverStatusEx.ServerId, statusLog.ServerId);
        Assert.AreEqual(serverStatusEx.FreeMemory, statusLog.FreeMemory);
        Assert.AreEqual(serverStatusEx.IsConfigure, statusLog.IsConfigure);
        Assert.AreEqual(serverStatusEx.TcpConnectionCount, statusLog.TcpConnectionCount);
        Assert.AreEqual(serverStatusEx.UdpConnectionCount, statusLog.UdpConnectionCount);
        Assert.AreEqual(serverStatusEx.SessionCount, statusLog.SessionCount);
        Assert.AreEqual(serverStatusEx.ThreadCount, statusLog.ThreadCount);
        Assert.IsTrue(dateTime <= statusLog.CreatedTime);

        //-----------
        // check: Check ServerStatus log is inserted
        //-----------
        var serverStatus = TestInit.NewServerStatus();

        dateTime = DateTime.UtcNow;
        await Task.Delay(500);
        await agentController1.StatusAsync(serverStatus);
        await agentController1.StatusAsync(serverStatus); // last status will not be synced
        await TestInit1.SyncToReport();
        statusLogs = await serverController.StatusLogsAsync(TestInit1.ProjectId, server.ServerId, recordCount: 100);
        statusLog = statusLogs.First();
        Assert.AreEqual(server.ServerId, statusLog.ServerId);
        Assert.AreEqual(serverStatus.FreeMemory, statusLog.FreeMemory);
        Assert.IsFalse(statusLog.IsConfigure);
        Assert.AreEqual(serverStatus.TcpConnectionCount, statusLog.TcpConnectionCount);
        Assert.AreEqual(serverStatus.UdpConnectionCount, statusLog.UdpConnectionCount);
        Assert.AreEqual(serverStatus.SessionCount, statusLog.SessionCount);
        Assert.AreEqual(serverStatus.ThreadCount, statusLog.ThreadCount);
        Assert.IsTrue(statusLog.CreatedTime > dateTime);
    }

    [TestMethod]
    public async Task Configure_reconfig()
    {
        var serverController = new ServerController(TestInit1.Http);
        var agentController = TestInit1.CreateAgentController();

        var serverId = TestInit1.ServerId1;
        var serverData = await serverController.ServersGetAsync(TestInit1.ProjectId, serverId);
        var oldCode = serverData.Server.ConfigCode;

        //-----------
        // check: update groupId should lead to reconfig
        //-----------
        await serverController.ServersPatchAsync(TestInit1.ProjectId, serverId, new ServerUpdateParams { AccessPointGroupId = new GuidNullablePatch { Value = TestInit1.AccessPointGroupId2 } });
        await serverController.ServersPatchAsync(TestInit1.ProjectId, serverId, new ServerUpdateParams { AccessPointGroupId = new GuidNullablePatch { Value = null } });
        serverData = await serverController.ServersGetAsync(TestInit1.ProjectId, serverId);
        Assert.AreNotEqual(oldCode, serverData.Server.ConfigCode);
        oldCode = serverData.Server.ConfigCode;

        //-----------
        // check: add an AccessPoint should lead to reconfig
        //-----------
        var accessPointController = new AccessPointController(TestInit1.Http);
        var accessPoint = await accessPointController.AccessPointsPostAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = serverId, 
                IpAddress = await TestInit1.NewIpV4String(),
                AccessPointGroupId = TestInit1.AccessPointGroupId2,
                IsListen = true
            });
        serverData = await serverController.ServersGetAsync(TestInit1.ProjectId, serverId);
        Assert.AreNotEqual(oldCode, serverData.Server.ConfigCode);
        oldCode = serverData.Server.ConfigCode;

        //-----------
        // check: updating AccessPoint should lead to reconfig
        //-----------
        await accessPointController.AccessPointsPatchAsync(TestInit1.ProjectId, accessPoint.AccessPointId,
            new AccessPointUpdateParams { IsListen = new BooleanPatch{Value = !accessPoint.IsListen }});
        serverData = await serverController.ServersGetAsync(TestInit1.ProjectId, serverId);
        Assert.AreNotEqual(oldCode, serverData.Server.ConfigCode);
        oldCode = serverData.Server.ConfigCode;

        //-----------
        // check: ConfigCode should not be reset by Configuring server with incorrect code
        //-----------
        var serverInfo1 = await TestInit1.NewServerInfo();
        serverInfo1.ConfigCode = Guid.NewGuid();
        await agentController.ConfigureAsync(serverInfo1);
        serverData = await serverController.ServersGetAsync(TestInit1.ProjectId, serverId);
        Assert.IsNotNull(serverData.Server.ConfigCode);

        //-----------
        // check: UpdateStatus should return ConfigCode
        //-----------
        var serverStatus = TestInit.NewServerStatus();
        var serverCommand = await agentController.StatusAsync(serverStatus);
        Assert.AreEqual(serverCommand.ConfigCode, oldCode);

        //-----------
        // check: ConfigCode should be rest by Configuring server with correct code
        //-----------
        serverInfo1.ConfigCode = oldCode;
        await agentController.ConfigureAsync(serverInfo1);
        serverData = await serverController.ServersGetAsync(TestInit1.ProjectId, serverId);
        Assert.IsNull(serverData.Server.ConfigCode);

        //-----------
        // check: After configure with correct code, UpdateStatus should return null ConfigCode
        //-----------
        serverStatus = TestInit.NewServerStatus();
        serverCommand = await agentController.StatusAsync(serverStatus);
        Assert.IsNull(serverCommand.ConfigCode);
    }

    [TestMethod]
    public async Task Configure_on_auto_update_accessPoints()
    {
        // create serverInfo
        var accessPointGroupController = new AccessPointGroupController(TestInit1.Http);

        var accessPointGroup1 = await accessPointGroupController.AccessPointGroupsPostAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        var serverController = new ServerController(TestInit1.Http);
        var server = await serverController.ServersPostAsync(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = accessPointGroup1.AccessPointGroupId });

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
        serverInfo.PrivateIpAddresses = new[] { await TestInit1.NewIpV4String(), await TestInit1.NewIpV6String() };
        serverInfo.PublicIpAddresses = new[] { await TestInit1.NewIpV4String(), await TestInit1.NewIpV6String(), publicInTokenAccessPoint2.IpAddress };

        //Configure
        var agentController = TestInit1.CreateAgentController(server.ServerId);
        await agentController.ConfigureAsync(serverInfo);
        var accessPointController = new AccessPointController(TestInit1.Http);
        var accessPoints = await accessPointController.AccessPointsGetAsync(TestInit1.ProjectId, server.ServerId, null);
        Assert.AreEqual(publicInTokenAccessPoint2.IpAddress,
            accessPoints.Single(x => x.AccessPointMode == AccessPointMode.PublicInToken).IpAddress);

        // --------
        // Check: another server with same group should not have any PublicInTokenAccess
        // --------
        server = await serverController.ServersPostAsync(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = accessPointGroup1.AccessPointGroupId });
        var publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(server);
        Assert.IsNull(publicInTokenAccessPoint);

        // --------
        // Check: another server with different group should have one PublicInTokenAccess
        // --------
        var accessPointGroup2 = await accessPointGroupController.AccessPointGroupsPostAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        server = await serverController.ServersPostAsync(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = accessPointGroup2.AccessPointGroupId });
        publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(server);
        Assert.IsNotNull(publicInTokenAccessPoint);
    }

    // return the only PublicInToken AccessPoint
    public async Task<Api.AccessPoint?> Configure_auto_update_accessPoints_on_internal(Api.Server server)
    {
        var accessPointController = new AccessPointController(TestInit1.Http);

        // create serverInfo
        var serverInfo = await TestInit1.NewServerInfo();
        var publicIp = await TestInit1.NewIpV6();
        var privateIp = await TestInit1.NewIpV4();
        serverInfo.PrivateIpAddresses = new[] { publicIp.ToString(), privateIp.ToString(), (await TestInit1.NewIpV6()).ToString(), privateIp.ToString() };
        serverInfo.PublicIpAddresses = new[] { publicIp.ToString(), (await TestInit1.NewIpV4()).ToString(), (await TestInit1.NewIpV6()).ToString() };

        //Configure
        var agentController = TestInit1.CreateAgentController(server.ServerId);
        var serverConfig = await agentController.ConfigureAsync(serverInfo);
        Assert.AreEqual(TestInit1.AppOptions.ServerUpdateStatusInterval, TimeSpan.FromSeconds(serverConfig.UpdateStatusInterval));
        Assert.AreEqual(serverConfig.TcpEndPoints.Count, serverConfig.TcpEndPoints.Distinct().Count(), "Duplicate listener!");

        //-----------
        // check: Configure with AutoUpdate is true (Server.AccessPointGroupId is set)
        //-----------
        var accessPoints = (await accessPointController.AccessPointsGetAsync(TestInit1.ProjectId, server.ServerId, null)).ToArray();
        var totalServerInfoIpAddress = serverInfo.PrivateIpAddresses.Concat(serverInfo.PublicIpAddresses).Distinct().Count();
        Assert.AreEqual(totalServerInfoIpAddress, accessPoints.Length);

        // private[0]
        var accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[0]);
        var accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken, "shared publicIp and privateIp must be see as publicIp");
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x == accessEndPoint.ToString()));

        // private[1]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[1]);
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen);
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x == accessEndPoint.ToString()));

        // private[2]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[2]);
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen);
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x == accessEndPoint.ToString()));

        // public[0]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[0]);
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x == accessEndPoint.ToString()));

        // public[1]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[1]);
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsFalse(accessPoint.IsListen);
        Assert.IsFalse(serverConfig.TcpEndPoints.Any(x => x == accessEndPoint.ToString()));

        // public[2]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[2]);
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsFalse(accessPoint.IsListen);
        Assert.IsFalse(serverConfig.TcpEndPoints.Any(x => x == accessEndPoint.ToString()));

        // PublicInToken should never be deleted
        return accessPoints.SingleOrDefault(x => x.AccessPointMode == AccessPointMode.PublicInToken);
    }

    [TestMethod]
    public async Task Configure_off_auto_update_accessPoints()
    {
        // create serverInfo
        var serverController = new ServerController(TestInit1.Http);
        var server = await serverController.ServersPostAsync(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = null });

        var accessPointController = new AccessPointController(TestInit1.Http);
        var accessPoint1 = await accessPointController.AccessPointsPostAsync(server.ProjectId,
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

        var accessPoint2 = await accessPointController.AccessPointsPostAsync(server.ProjectId,
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
        var publicIp = await TestInit1.NewIpV6String();
        serverInfo1.PrivateIpAddresses = new[] { publicIp, await TestInit1.NewIpV4String(), await TestInit1.NewIpV6String() };
        serverInfo1.PublicIpAddresses = new[] { publicIp, await TestInit1.NewIpV4String(), await TestInit1.NewIpV6String() };

        // Configure
        var agentController1 = TestInit1.CreateAgentController(server.ServerId);
        await agentController1.ConfigureAsync(serverInfo1);

        // Test that accessPoints have not been changed
        var accessPoints = await accessPointController.AccessPointsGetAsync(TestInit1.ProjectId, server.ServerId, null);
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
        public TestServer(TestInit testInit, Guid groupId, bool configure = true, bool sendStatus = true, IPEndPoint? serverEndPoint = null)
        {
            var accessPointController = new AccessPointController(testInit.Http);
            var serverController = new ServerController(testInit.Http);

            ServerEndPoint = serverEndPoint ?? testInit.NewEndPoint().Result;
            Server = serverController.ServersPostAsync(testInit.ProjectId, new ServerCreateParams()).Result;
            accessPointController.AccessPointsPostAsync(testInit.ProjectId,
                new AccessPointCreateParams
                {
                    ServerId = Server.ServerId,
                    IpAddress = ServerEndPoint.Address.ToString(),
                    AccessPointGroupId = groupId,
                    AccessPointMode = AccessPointMode.Public,
                    TcpPort = ServerEndPoint.Port,
                    IsListen = true
                }).Wait();
            AgentController = testInit.CreateAgentController(Server.ServerId);
            ServerStatus.SessionCount = 0;

            if (configure)
            {
                var serverInfo = testInit.NewServerInfo().Result;
                serverInfo.Status = ServerStatus;
                AgentController.ConfigureAsync(serverInfo).Wait();
                if (sendStatus)
                    AgentController.StatusAsync(serverInfo.Status).Wait();
            }
        }

        public IPEndPoint ServerEndPoint { get; }
        public Api.Server Server { get; }
        public AgentController AgentController { get; }
        public ServerStatus ServerStatus { get; } = TestInit.NewServerStatus();
    }

    [TestMethod]
    public async Task LoadBalancer()
    {
        var accessPointGroupController = new AccessPointGroupController(TestInit1.Http);
        var accessTokenController = new AccessTokenController(TestInit1.Http);
        var accessPointGroup = await accessPointGroupController.AccessPointGroupsPostAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        TestInit1.WebApp.Services.GetRequiredService<ServerManager>().AllowRedirect = true; // enable load balancer

        // Create and init servers
        var testServers = new List<TestServer>();
        for (var i = 0; i < 4; i++)
        {
            var testServer = new TestServer(TestInit1, accessPointGroup.AccessPointGroupId, i != 3);
            testServers.Add(testServer);
        }
        testServers.Add(new TestServer(TestInit1, accessPointGroup.AccessPointGroupId, true, true, await TestInit1.NewEndPointIp6()));
        testServers.Add(new TestServer(TestInit1, accessPointGroup.AccessPointGroupId, true, false));

        // create access token
        var accessToken = await accessTokenController.AccessTokensPostAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = accessPointGroup.AccessPointGroupId,
            });

        // create sessions
        var agentController = TestInit1.CreateAgentController(testServers[0].Server.ServerId);
        for (var i = 0; i < 9; i++)
        {
            var testServer = testServers[0];
            var sessionRequestEx = TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: testServer.ServerEndPoint);
            var sessionResponseEx = await agentController.SessionsPostAsync(sessionRequestEx);
            if (sessionResponseEx.ErrorCode == SessionErrorCode.RedirectHost)
            {
                Assert.IsNotNull(sessionResponseEx.RedirectHostEndPoint);
                sessionRequestEx.HostEndPoint = sessionResponseEx.RedirectHostEndPoint;
                testServer = testServers.First(x => sessionResponseEx.RedirectHostEndPoint.Equals(x.ServerEndPoint.ToString()));
                sessionResponseEx = await testServer.AgentController.SessionsPostAsync(sessionRequestEx);
            }

            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
            testServer.ServerStatus.SessionCount++;
            await testServer.AgentController.StatusAsync(testServer.ServerStatus);
        }

        // some server should not be selected
        Assert.AreEqual(0, testServers[3].ServerStatus.SessionCount, "A server with configuring state is selected");
        Assert.AreEqual(0, testServers[4].ServerStatus.SessionCount, "IpVersion is not respected");
        Assert.AreEqual(0, testServers[5].ServerStatus.SessionCount, "Should not use server in Configuring state");

        // each server sessions must be 3
        Assert.AreEqual(3, testServers[0].ServerStatus.SessionCount);
        Assert.AreEqual(3, testServers[1].ServerStatus.SessionCount);
        Assert.AreEqual(3, testServers[2].ServerStatus.SessionCount);
    }
}