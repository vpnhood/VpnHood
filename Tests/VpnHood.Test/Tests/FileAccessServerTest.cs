using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access.Managers.File;
using VpnHood.Server.Access.Managers.Http;

namespace VpnHood.Test.Tests;

[TestClass]
public class FileAccessManagerTest : TestBase
{
    [TestMethod]
    public void GetSslCertificateData()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var fileAccessManager = TestHelper.CreateFileAccessManager(storagePath: storagePath);

        // Create accessManager
        using TestEmbedIoAccessManager testHttpAccessManager = new(fileAccessManager);
        var accessManager = new HttpAccessManager(new HttpAccessManagerOptions(testHttpAccessManager.BaseUri, "Bearer xxx"));

        // ************
        // *** TEST ***: default cert must be used
        var cert1 = new X509Certificate2(accessManager.GetSslCertificateData(IPEndPoint.Parse("2.2.2.2:443")).Result);
        Assert.AreEqual(cert1.Thumbprint, fileAccessManager.DefaultCert.Thumbprint);
    }

    [TestMethod]
    public async Task Crud()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var fileAccessManagerOptions = new FileAccessManagerOptions
        {
            TcpEndPoints = [new IPEndPoint(IPAddress.Any, 8000)],
            PublicEndPoints = [IPEndPoint.Parse("127.0.0.1:8000")]
        };
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
    public void AddUsage()
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var accessManager1 = TestHelper.CreateFileAccessManager(storagePath: storagePath);

        //add token
        var accessItem1 = accessManager1.AccessItem_Create();
        var sessionRequestEx1 = TestHelper.CreateSessionRequestEx(accessItem1.Token);

        // create a session
        var sessionResponse = accessManager1.Session_Create(sessionRequestEx1).Result;
        Assert.IsNotNull(sessionResponse, "access has not been retrieved");

        // ************
        // *** TEST ***: add sent and receive bytes
        var response = accessManager1.Session_AddUsage(sessionResponse.SessionId,
            new Traffic { Sent = 20, Received = 10 }).Result;
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(20, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(10, response.AccessUsage?.Traffic.Received);

        response = accessManager1.Session_AddUsage(sessionResponse.SessionId,
            new Traffic { Sent = 20, Received = 10 }).Result;
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(40, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(20, response.AccessUsage?.Traffic.Received);

        response = accessManager1.Session_Get(sessionResponse.SessionId, sessionRequestEx1.HostEndPoint,
            sessionRequestEx1.ClientIp).Result;
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(40, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(20, response.AccessUsage?.Traffic.Received);

        // close session
        response = accessManager1.Session_Close(sessionResponse.SessionId,
            new Traffic { Sent = 20, Received = 10 }).Result;
        Assert.AreEqual(SessionErrorCode.SessionClosed, response.ErrorCode, response.ErrorMessage);
        Assert.AreEqual(60, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(30, response.AccessUsage?.Traffic.Received);

        // check is session closed
        response = accessManager1.Session_Get(sessionResponse.SessionId, sessionRequestEx1.HostEndPoint,
            sessionRequestEx1.ClientIp).Result;
        Assert.AreEqual(SessionErrorCode.SessionClosed, response.ErrorCode);
        Assert.AreEqual(60, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(30, response.AccessUsage?.Traffic.Received);

        // check restore
        var accessManager2 = TestHelper.CreateFileAccessManager(storagePath: storagePath);
        response = accessManager2.Session_Create(sessionRequestEx1).Result;
        Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);
        Assert.AreEqual(60, response.AccessUsage?.Traffic.Sent);
        Assert.AreEqual(30, response.AccessUsage?.Traffic.Received);
    }
}