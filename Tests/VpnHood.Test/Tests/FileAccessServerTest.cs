using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access.Managers.FileAccessManagers;

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
        var sessionRequestEx1 = TestHelper.CreateSessionRequestEx(token1);
        sessionRequestEx1.ExtraData = "1234";

        var token2 = accessManager1.CreateToken();
        var sessionRequestEx2 = TestHelper.CreateSessionRequestEx(token2);

        var token3 = accessManager1.CreateToken();

        // ************
        // *** TEST ***: get all tokensId
        var accessTokenDatas = await accessManager1.AccessTokenService.List();
        Assert.IsTrue(accessTokenDatas.Any(x => x.AccessToken.TokenId == token1.TokenId));
        Assert.IsTrue(accessTokenDatas.Any(x => x.AccessToken.TokenId == token2.TokenId));
        Assert.IsTrue(accessTokenDatas.Any(x => x.AccessToken.TokenId == token3.TokenId));
        Assert.AreEqual(3, accessTokenDatas.Length);


        // ************
        // *** TEST ***: token must be retrieved with TokenId
        var sessionResponseEx1 = await accessManager1.Session_Create(sessionRequestEx1);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode, "access has not been retrieved");

        // ************
        // *** TEST: Get AdditionalDat
        var sessionResponse = await accessManager1.Session_Get(sessionResponseEx1.SessionId,
            sessionRequestEx1.HostEndPoint, sessionRequestEx1.ClientIp);
        Assert.AreEqual(sessionRequestEx1.ExtraData, sessionResponse.ExtraData);
        Assert.AreEqual(sessionRequestEx1.ClientInfo.ProtocolVersion, sessionResponse.ProtocolVersion);

        // ************
        // *** TEST ***: Removing token
        accessManager1.AccessTokenService.Delete(token1.TokenId).Wait();
        accessTokenDatas = await accessManager1.AccessTokenService.List();
        Assert.IsFalse(accessTokenDatas.Any(x => x.AccessToken.TokenId == token1.TokenId));
        Assert.IsTrue(accessTokenDatas.Any(x => x.AccessToken.TokenId == token2.TokenId));
        Assert.IsTrue(accessTokenDatas.Any(x => x.AccessToken.TokenId == token3.TokenId));
        Assert.AreEqual(2, accessTokenDatas.Length);
        Assert.AreEqual(accessManager1.Session_Create(sessionRequestEx1).Result.ErrorCode,
            SessionErrorCode.AccessError);

        // ************
        // *** TEST ***: token must be retrieved by new instance after reloading (last operation is remove)
        var accessManager2 = new FileAccessManager(storagePath, fileAccessManagerOptions);

        accessTokenDatas = await accessManager2.AccessTokenService.List();
        Assert.IsTrue(accessTokenDatas.Any(x => x.AccessToken.TokenId == token2.TokenId));
        Assert.IsTrue(accessTokenDatas.Any(x => x.AccessToken.TokenId == token3.TokenId));
        Assert.AreEqual(2, accessTokenDatas.Length);

        // ************
        // *** TEST ***: token must be retrieved with TokenId
        Assert.AreEqual(SessionErrorCode.Ok, accessManager2.Session_Create(sessionRequestEx2).Result.ErrorCode,
            "Access has not been retrieved");

        // ************
        // *** TEST ***: token must be retrieved after reloading
        accessManager1.CreateToken();
        var accessManager3 = new FileAccessManager(storagePath, fileAccessManagerOptions);
        accessTokenDatas = await accessManager3.AccessTokenService.List();
        Assert.AreEqual(3, accessTokenDatas.Length);
        Assert.AreEqual(SessionErrorCode.Ok, accessManager3.Session_Create(sessionRequestEx2).Result.ErrorCode,
            "access has not been retrieved");
    }

    [TestMethod]
    public async Task AddUsage()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var accessManager1 = TestHelper.CreateAccessManager(storagePath: storagePath);

        //add token
        var token = accessManager1.CreateToken();
        var sessionRequestEx1 = TestHelper.CreateSessionRequestEx(token);

        // create a session
        var sessionResponse = await accessManager1.Session_Create(sessionRequestEx1);
        Assert.IsNotNull(sessionResponse, "access has not been retrieved");

        // ************
        // *** TEST ***: add sent and receive bytes
        var response = await accessManager1.Session_AddUsage(sessionResponse.SessionId,
            new Traffic { Sent = 20, Received = 10 }, null);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(20, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(10, response.AccessUsage?.Traffic.Received);

        response = await accessManager1.Session_AddUsage(sessionResponse.SessionId,
            new Traffic { Sent = 20, Received = 10 }, null);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(40, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(20, response.AccessUsage?.Traffic.Received);

        response = await accessManager1.Session_Get(sessionResponse.SessionId, sessionRequestEx1.HostEndPoint,
            sessionRequestEx1.ClientIp);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(40, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(20, response.AccessUsage?.Traffic.Received);

        // close session
        response = await accessManager1.Session_Close(sessionResponse.SessionId,
            new Traffic { Sent = 20, Received = 10 });
        Assert.AreEqual(SessionErrorCode.SessionClosed, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(60, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(30, response.AccessUsage?.Traffic.Received);

        // check is session closed
        response = await accessManager1.Session_Get(sessionResponse.SessionId, sessionRequestEx1.HostEndPoint,
            sessionRequestEx1.ClientIp);
        Assert.AreEqual(SessionErrorCode.SessionClosed, response.ErrorCode);
        Assert.AreEqual(60, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(30, response.AccessUsage?.Traffic.Received);

        // check restore
        var accessManager2 = TestHelper.CreateAccessManager(storagePath: storagePath);
        response = await accessManager2.Session_Create(sessionRequestEx1);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);
        Assert.AreEqual(60, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(30, response.AccessUsage?.Traffic.Received);
    }
}