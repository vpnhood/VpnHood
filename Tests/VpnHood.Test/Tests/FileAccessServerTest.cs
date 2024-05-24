using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access.Managers.File;

namespace VpnHood.Test.Tests;

[TestClass]
public class FileAccessManagerTest : TestBase
{
     [TestMethod]
    public async Task Create_access_token_with_valid_domain()
    {
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.IsValidHostName = true;

        var fileAccessManager = TestHelper.CreateFileAccessManager(options);
        using var testAccessManager = new TestAccessManager(fileAccessManager);
        await using var server = await TestHelper.CreateServer(testAccessManager);

        var accessItem = fileAccessManager.AccessItem_Create();
        Assert.AreEqual(fileAccessManager.ServerConfig.TcpEndPointsValue.First().Port, accessItem.Token.ServerToken.HostPort);
        Assert.AreEqual(fileAccessManager.ServerConfig.IsValidHostName, accessItem.Token.ServerToken.IsValidHostName);
        Assert.IsNull(accessItem.Token.ServerToken.CertificateHash);
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
        var accessItem1 = accessManager1.AccessItem_Create();
        var sessionRequestEx1 = TestHelper.CreateSessionRequestEx(accessItem1.Token);
        sessionRequestEx1.ExtraData = "1234";

        var accessItem2 = accessManager1.AccessItem_Create();
        var sessionRequestEx2 = TestHelper.CreateSessionRequestEx(accessItem2.Token);

        var accessItem3 = accessManager1.AccessItem_Create();

        // ************
        // *** TEST ***: get all tokensId
        var accessItems = await accessManager1.AccessItem_LoadAll();
        Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem1.Token.TokenId));
        Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem2.Token.TokenId));
        Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem3.Token.TokenId));
        Assert.AreEqual(3, accessItems.Length);


        // ************
        // *** TEST ***: token must be retrieved with TokenId
        var sessionResponseEx1 = await accessManager1.Session_Create(sessionRequestEx1);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode, "access has not been retrieved");

        // ************
        // *** TEST: Get AdditionalDat
        var sessionResponse = await accessManager1.Session_Get(sessionResponseEx1.SessionId, sessionRequestEx1.HostEndPoint, sessionRequestEx1.ClientIp);
        Assert.AreEqual(sessionRequestEx1.ExtraData, sessionResponse.ExtraData);

        // ************
        // *** TEST ***: Removing token
        accessManager1.AccessItem_Delete(accessItem1.Token.TokenId).Wait();
        accessItems = await accessManager1.AccessItem_LoadAll();
        Assert.IsFalse(accessItems.Any(x => x.Token.TokenId == accessItem1.Token.TokenId));
        Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem2.Token.TokenId));
        Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem3.Token.TokenId));
        Assert.AreEqual(2, accessItems.Length);
        Assert.AreEqual(accessManager1.Session_Create(sessionRequestEx1).Result.ErrorCode,
            SessionErrorCode.AccessError);

        // ************
        // *** TEST ***: token must be retrieved by new instance after reloading (last operation is remove)
        var accessManager2 = new FileAccessManager(storagePath, fileAccessManagerOptions);

        accessItems = await accessManager2.AccessItem_LoadAll();
        Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem2.Token.TokenId));
        Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem3.Token.TokenId));
        Assert.AreEqual(2, accessItems.Length);

        // ************
        // *** TEST ***: token must be retrieved with TokenId
        Assert.AreEqual(SessionErrorCode.Ok, accessManager2.Session_Create(sessionRequestEx2).Result.ErrorCode,
            "Access has not been retrieved");

        // ************
        // *** TEST ***: token must be retrieved after reloading
        accessManager1.AccessItem_Create();
        var accessManager3 = new FileAccessManager(storagePath, fileAccessManagerOptions);
        accessItems = await accessManager3.AccessItem_LoadAll();
        Assert.AreEqual(3, accessItems.Length);
        Assert.AreEqual(SessionErrorCode.Ok, accessManager3.Session_Create(sessionRequestEx2).Result.ErrorCode,
            "access has not been retrieved");
    }

    [TestMethod]
    public async Task AddUsage()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var accessManager1 = TestHelper.CreateFileAccessManager(storagePath: storagePath);

        //add token
        var accessItem1 = accessManager1.AccessItem_Create();
        var sessionRequestEx1 = TestHelper.CreateSessionRequestEx(accessItem1.Token);

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
        var accessManager2 = TestHelper.CreateFileAccessManager(storagePath: storagePath);
        response = await accessManager2.Session_Create(sessionRequestEx1);
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);
        Assert.AreEqual(60, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(30, response.AccessUsage?.Traffic.Received);
    }
}