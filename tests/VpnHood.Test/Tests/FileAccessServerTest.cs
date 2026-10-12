using System.Net;
using System.Text.Json.Nodes;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class FileAccessManagerTest : TestBase
{
    [TestMethod]
    public async Task Create_access_token_with_valid_domain()
    {
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.IsValidHostName = true;

        var accessManager = TestHelper.CreateAccessManager(options);
        await using var server = await TestHelper.CreateServer(accessManager);

        var accessToken = accessManager.AccessTokenService.Create();
        var token = accessManager.GetToken(accessToken);
        Assert.AreEqual(accessManager.ServerConfig.TcpEndPointsValue.First().Port, token.ServerToken.HostPort);
        Assert.AreEqual(accessManager.ServerConfig.IsValidHostName, token.ServerToken.IsValidHostName);
        Assert.IsNull(token.ServerToken.CertificateHash);
    }

    [TestMethod]
    public async Task Crud()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.TcpEndPoints = [new IPEndPoint(IPAddress.Any, 8000)];
        fileAccessManagerOptions.PublicEndPoints = [IPEndPoint.Parse("127.0.0.1:8000")];
        var accessManager1 = new FileAccessManager(storagePath, fileAccessManagerOptions);

        //add two tokens
        var token1 = accessManager1.CreateToken();
        var sessionRequestEx1 = CreateSessionRequestEx(token1);
        sessionRequestEx1.ExtraData = "1234";

        var token2 = accessManager1.AccessTokenService.Create(maxSpeedMbps: new Traffic(sent: 5, received: 50));
        var sessionRequestEx2 = CreateSessionRequestEx(accessManager1.GetToken(token2));

        var token3 = accessManager1.CreateToken();

        // ************
        // *** TEST ***: get all tokensId
        var accessTokenDatas = await accessManager1.AccessTokenService.List(TestCt);
        Assert.Contains(x => x.AccessToken.TokenId == token1.TokenId, accessTokenDatas);
        Assert.Contains(x => x.AccessToken.TokenId == token2.TokenId, accessTokenDatas);
        Assert.Contains(x => x.AccessToken.TokenId == token3.TokenId, accessTokenDatas);
        Assert.HasCount(3, accessTokenDatas);


        // ************
        // *** TEST ***: token must be retrieved with TokenId
        var sessionResponseEx1 = await accessManager1.Session_Create(sessionRequestEx1, TestCt);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode, "access has not been retrieved");

        // ************
        // *** TEST: Get AdditionalDat
        var sessionResponse = await accessManager1.Session_Get(sessionResponseEx1.SessionId,
            sessionRequestEx1.HostEndPoint, sessionRequestEx1.ClientIp, TestCt);
        Assert.AreEqual(sessionRequestEx1.ExtraData, sessionResponse.ExtraData);
        Assert.AreEqual(sessionRequestEx1.ProtocolVersion, sessionResponse.ProtocolVersion);

        // ************
        // *** TEST ***: Removing token
        accessManager1.AccessTokenService.Delete(token1.TokenId, TestCt).Wait();
        accessTokenDatas = await accessManager1.AccessTokenService.List(TestCt);
        Assert.DoesNotContain(x => x.AccessToken.TokenId == token1.TokenId, accessTokenDatas);
        Assert.Contains(x => x.AccessToken.TokenId == token2.TokenId, accessTokenDatas);
        Assert.Contains(x => x.AccessToken.TokenId == token3.TokenId, accessTokenDatas);
        Assert.HasCount(2, accessTokenDatas);
        Assert.AreEqual(SessionErrorCode.AccessError,
            (await accessManager1.Session_Create(sessionRequestEx1, TestCt)).ErrorCode);

        // ************
        // *** TEST ***: token must be retrieved by new instance after reloading (last operation is remove)
        var accessManager2 = new FileAccessManager(storagePath, fileAccessManagerOptions);

        accessTokenDatas = await accessManager2.AccessTokenService.List(TestCt);
        Assert.Contains(x => x.AccessToken.TokenId == token2.TokenId, accessTokenDatas);
        Assert.Contains(x => x.AccessToken.TokenId == token3.TokenId, accessTokenDatas);
        Assert.HasCount(2, accessTokenDatas);

        // ************
        // *** TEST ***: token must be retrieved with TokenId
        var sessionResponseEx2 = await accessManager2.Session_Create(sessionRequestEx2, TestCt);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx2.ErrorCode,
            "Access has not been retrieved");
        Assert.AreEqual(new Traffic(sent: 5, received: 50), sessionResponseEx2.AccessInfo?.MaxSpeedMbps);

        // ************
        // *** TEST ***: token must be retrieved after reloading
        accessManager1.CreateToken();
        var accessManager3 = new FileAccessManager(storagePath, fileAccessManagerOptions);
        accessTokenDatas = await accessManager3.AccessTokenService.List(TestCt);
        Assert.HasCount(3, accessTokenDatas);
        Assert.AreEqual(new Traffic(sent: 5, received: 50),
            accessTokenDatas.Single(x => x.AccessToken.TokenId == token2.TokenId).AccessToken.MaxSpeedMbps);
        Assert.AreEqual(SessionErrorCode.Ok, (await accessManager3.Session_Create(sessionRequestEx2, TestCt)).ErrorCode,
            "access has not been retrieved");
    }

    [TestMethod]
    public async Task AddUsage()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var accessManager1 = TestHelper.CreateAccessManager(storagePath: storagePath);

        //add token
        var token = accessManager1.CreateToken();
        var sessionRequestEx1 = CreateSessionRequestEx(token);

        // create a session
        var sessionResponse = await accessManager1.Session_Create(sessionRequestEx1, TestCt);
        Assert.IsNotNull(sessionResponse, "access has not been retrieved");

        // ************
        // *** TEST ***: add sent and receive bytes
        var response = await accessManager1.Session_AddUsage(sessionResponse.SessionId,
            new Traffic { Sent = 20, Received = 10 }, null, TestCt);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(20, response.AccessUsage?.CycleTraffic.Sent);
        Assert.AreEqual(10, response.AccessUsage?.CycleTraffic.Received);

        response = await accessManager1.Session_AddUsage(sessionResponse.SessionId,
            new Traffic { Sent = 20, Received = 10 }, null, TestCt);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(40, response.AccessUsage?.CycleTraffic.Sent);
        Assert.AreEqual(20, response.AccessUsage?.CycleTraffic.Received);

        response = await accessManager1.Session_Get(sessionResponse.SessionId, sessionRequestEx1.HostEndPoint,
            sessionRequestEx1.ClientIp, TestCt);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(40, response.AccessUsage?.CycleTraffic.Sent);
        Assert.AreEqual(20, response.AccessUsage?.CycleTraffic.Received);

        // close session
        response = await accessManager1.Session_Close(sessionResponse.SessionId,
            new Traffic { Sent = 20, Received = 10 }, TestCt);
        Assert.AreEqual(SessionErrorCode.SessionClosed, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(60, response.AccessUsage?.CycleTraffic.Sent);
        Assert.AreEqual(30, response.AccessUsage?.CycleTraffic.Received);

        // check is session closed
        response = await accessManager1.Session_Get(sessionResponse.SessionId, sessionRequestEx1.HostEndPoint,
            sessionRequestEx1.ClientIp, TestCt);
        Assert.AreEqual(SessionErrorCode.SessionClosed, response.ErrorCode);
        Assert.AreEqual(60, response.AccessUsage?.CycleTraffic.Sent);
        Assert.AreEqual(30, response.AccessUsage?.CycleTraffic.Received);

        // check restore
        var accessManager2 = TestHelper.CreateAccessManager(storagePath: storagePath);
        response = await accessManager2.Session_Create(sessionRequestEx1, TestCt);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);
        Assert.AreEqual(60, response.AccessUsage?.CycleTraffic.Sent);
        Assert.AreEqual(30, response.AccessUsage?.CycleTraffic.Received);
    }

    [TestMethod]
    public async Task A_restart_keeps_the_end_of_a_clients_old_sessions()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        using var accessManager1 = TestHelper.CreateAccessManager(storagePath: storagePath);
        var sessionRequestEx = CreateSessionRequestEx(accessManager1.CreateToken());

        // one client: its first session closed, its second suppressed by its third, the live one
        var closedSession = await accessManager1.Session_Create(sessionRequestEx, TestCt);
        await accessManager1.Session_Close(closedSession.SessionId, new Traffic(), TestCt);
        var suppressedSession = await accessManager1.Session_Create(sessionRequestEx, TestCt);
        var liveSession = await accessManager1.Session_Create(sessionRequestEx, TestCt);

        // a restarted server's recovery asks for every session. Loaded as live, an ended session suppressed the
        // client's live one, which the server then closed
        using var accessManager2 = TestHelper.CreateAccessManager(storagePath: storagePath);
        var responses = await accessManager2.Session_GetAll(TestCt);
        Assert.AreEqual(SessionErrorCode.Ok, responses.Single(x => x.SessionId == liveSession.SessionId).ErrorCode);
        Assert.AreEqual(SessionErrorCode.SessionClosed,
            responses.Single(x => x.SessionId == closedSession.SessionId).ErrorCode);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy,
            responses.Single(x => x.SessionId == suppressedSession.SessionId).ErrorCode);
    }

    [TestMethod]
    public async Task A_used_session_times_out_from_its_last_use_not_its_creation()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        using var accessManager1 = TestHelper.CreateAccessManager(storagePath: storagePath);
        var token = accessManager1.CreateToken(maxClientCount: 2);
        var usedSession = await accessManager1.Session_Create(CreateSessionRequestEx(token), TestCt);
        var unusedSession = await accessManager1.Session_Create(CreateSessionRequestEx(token), TestCt);

        // both made 47 hours ago, as their files hold them; a restart loads them
        var oldTime = DateTime.UtcNow.AddHours(-47);
        SetSessionTimes(accessManager1.SessionsFolderPath, usedSession.SessionId, oldTime);
        SetSessionTimes(accessManager1.SessionsFolderPath, unusedSession.SessionId, oldTime);
        using var accessManager2 = TestHelper.CreateAccessManager(storagePath: storagePath);

        // the server's report of a session's traffic is its use; the start's recovery is none
        await accessManager2.Session_GetAll(TestCt);
        await accessManager2.Session_AddUsage(usedSession.SessionId, new Traffic { Sent = 10, Received = 10 }, null,
            TestCt);
        Assert.IsGreaterThan(DateTime.UtcNow.AddMinutes(-1), GetLastUsedTime(accessManager2, usedSession.SessionId));
        Assert.IsLessThan(oldTime.AddMinutes(1), GetLastUsedTime(accessManager2, unusedSession.SessionId));

        // a restart keeps the use
        using var accessManager3 = TestHelper.CreateAccessManager(storagePath: storagePath);
        Assert.IsGreaterThan(DateTime.UtcNow.AddMinutes(-1), GetLastUsedTime(accessManager3, usedSession.SessionId));
    }

    // a session's times as its file would hold them had it been made then
    private static void SetSessionTimes(string sessionsFolderPath, ulong sessionId, DateTime time)
    {
        var filePath = Path.Combine(sessionsFolderPath, $"{sessionId}.session");
        var session = JsonNode.Parse(File.ReadAllText(filePath)) ??
                      throw new InvalidOperationException("The session file is empty.");
        session["CreatedTime"] = time;
        session["LastUsedTime"] = time;
        File.WriteAllText(filePath, session.ToJsonString());
    }

    private static DateTime GetLastUsedTime(FileAccessManager accessManager, ulong sessionId)
    {
        return accessManager.SessionService.Sessions[sessionId].LastUsedTime;
    }

    public SessionRequestEx CreateSessionRequestEx(Token token, string? clientId = null)
    {
        clientId ??= Guid.NewGuid().ToString();
        return new SessionRequestEx {
            TokenId = token.TokenId,
            ClientInfo = new ClientInfo {
                ClientId = clientId,
                UserAgent = "Test",
                ClientVersion = "1.0.0",
                MinProtocolVersion = 5,
                MaxProtocolVersion = 6
            },
            ProtocolVersion = 5,
            HostEndPoint = token.ServerToken.HostEndPoints!.First(),
            EncryptedClientId = VhUtils.EncryptClientId(clientId, token.Secret),
            ClientIp = null,
            ExtraData = null
        };
    }
}