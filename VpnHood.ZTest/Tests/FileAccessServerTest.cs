using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.AccessServers;
using VpnHood.Server.Messaging;

namespace VpnHood.Test.Tests
{
    [TestClass]
    public class FileAccessServerTest
    {
        [TestMethod]
        public void GetSslCertificateData()
        {
            var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var fileAccessServer = TestHelper.CreateFileAccessServer(storagePath: storagePath);
            var publicEndPoints = new[] { IPEndPoint.Parse("127.0.0.1:443") };

            // Create accessServer
            using TestEmbedIoAccessServer testRestAccessServer = new(fileAccessServer);
            var accessServer = new RestAccessServer(new RestAccessServerOptions(testRestAccessServer.BaseUri.AbsoluteUri, "Bearer xxx"));

            // ************
            // *** TEST ***: default cert must be used when there is no InternalEndPoint
            fileAccessServer.AccessItem_Create(publicEndPoints);
            var cert1 = new X509Certificate2(accessServer.GetSslCertificateData(IPEndPoint.Parse("2.2.2.2:443")).Result);
            Assert.AreEqual(cert1.Thumbprint, fileAccessServer.DefaultCert.Thumbprint);
        }

        private static SessionRequestEx CreateSessionRequestEx(FileAccessServer.AccessItem accessItem, Guid clientId)
        {
            return new SessionRequestEx(accessItem.Token.TokenId,
                new ClientInfo { ClientId = clientId },
                hostEndPoint: accessItem.Token.HostEndPoints!.First(),
                encryptedClientId: Util.EncryptClientId(clientId, accessItem.Token.Secret));
        }

        [TestMethod]
        public void Crud()
        {
            var hostEndPoints = new[] { IPEndPoint.Parse("127.0.0.1:8000") };
            var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var fileAccessServerOptions = new FileAccessServerOptions { TcpEndPoints = new[] { new IPEndPoint(IPAddress.Any, 8000) } };
            var accessServer1 = new FileAccessServer(storagePath, fileAccessServerOptions);

            //add two tokens
            var accessItem1 = accessServer1.AccessItem_Create(hostEndPoints);
            var sessionRequestEx1 = CreateSessionRequestEx(accessItem1, Guid.NewGuid());

            var accessItem2 = accessServer1.AccessItem_Create(hostEndPoints);
            var sessionRequestEx2 = CreateSessionRequestEx(accessItem2, Guid.NewGuid());

            var accessItem3 = accessServer1.AccessItem_Create(hostEndPoints);

            // ************
            // *** TEST ***: get all tokensId
            var accessItems = accessServer1.AccessItem_LoadAll();
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem1.Token.TokenId));
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem2.Token.TokenId));
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem3.Token.TokenId));
            Assert.AreEqual(3, accessItems.Length);


            // ************
            // *** TEST ***: token must be retrieved with TokenId
            Assert.AreEqual(SessionErrorCode.Ok, accessServer1.Session_Create(sessionRequestEx1).Result.ErrorCode,
                "access has not been retrieved");

            // ************
            // *** TEST ***: Removing token
            accessServer1.AccessItem_Delete(accessItem1.Token.TokenId).Wait();
            accessItems = accessServer1.AccessItem_LoadAll();
            Assert.IsFalse(accessItems.Any(x => x.Token.TokenId == accessItem1.Token.TokenId));
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem2.Token.TokenId));
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem3.Token.TokenId));
            Assert.AreEqual(2, accessItems.Length);
            Assert.AreEqual(accessServer1.Session_Create(sessionRequestEx1).Result.ErrorCode,
                SessionErrorCode.GeneralError);

            // ************
            // *** TEST ***: token must be retrieved by new instance after reloading (last operation is remove)
            var accessServer2 = new FileAccessServer(storagePath, fileAccessServerOptions);

            accessItems = accessServer2.AccessItem_LoadAll();
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem2.Token.TokenId));
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem3.Token.TokenId));
            Assert.AreEqual(2, accessItems.Length);

            // ************
            // *** TEST ***: token must be retrieved with TokenId
            Assert.AreEqual(SessionErrorCode.Ok, accessServer2.Session_Create(sessionRequestEx2).Result.ErrorCode,
                "Access has not been retrieved");

            // ************
            // *** TEST ***: token must be retrieved after reloading
            accessServer1.AccessItem_Create(hostEndPoints);
            var accessServer3 = new FileAccessServer(storagePath, fileAccessServerOptions);
            accessItems = accessServer3.AccessItem_LoadAll();
            Assert.AreEqual(3, accessItems.Length);
            Assert.AreEqual(SessionErrorCode.Ok, accessServer3.Session_Create(sessionRequestEx2).Result.ErrorCode,
                "access has not been retrieved");
        }

        [TestMethod]
        public void AddUsage()
        {
            var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var accessServer1 = TestHelper.CreateFileAccessServer(storagePath: storagePath);
            var hostEndPoints = new[] { IPEndPoint.Parse("1.1.1.1:443") };

            //add token
            var accessItem1 = accessServer1.AccessItem_Create(hostEndPoints);
            var sessionRequestEx1 = CreateSessionRequestEx(accessItem1, Guid.NewGuid());

            // create a session
            var sessionResponse = accessServer1.Session_Create(sessionRequestEx1).Result;
            Assert.IsNotNull(sessionResponse, "access has not been retrieved");

            // ************
            // *** TEST ***: add sent and receive bytes
            var response = accessServer1.Session_AddUsage(sessionResponse.SessionId, false,
                new UsageInfo { SentTraffic = 20, ReceivedTraffic = 10 }).Result;
            Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
            Assert.AreEqual(20, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, response.AccessUsage?.ReceivedTraffic);

            response = accessServer1.Session_AddUsage(sessionResponse.SessionId, false,
                new UsageInfo { SentTraffic = 20, ReceivedTraffic = 10 }).Result;
            Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
            Assert.AreEqual(40, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(20, response.AccessUsage?.ReceivedTraffic);

            response = accessServer1.Session_Get(sessionResponse.SessionId, sessionRequestEx1.HostEndPoint,
                sessionRequestEx1.ClientIp).Result;
            Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
            Assert.AreEqual(40, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(20, response.AccessUsage?.ReceivedTraffic);

            // close session
            response = accessServer1.Session_AddUsage(sessionResponse.SessionId, true,
                new UsageInfo { SentTraffic = 20, ReceivedTraffic = 10 }).Result;
            Assert.AreEqual(SessionErrorCode.SessionClosed, response.ErrorCode, response.ErrorMessage);
            Assert.AreEqual(60, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(30, response.AccessUsage?.ReceivedTraffic);

            // check is session closed
            response = accessServer1.Session_Get(sessionResponse.SessionId, sessionRequestEx1.HostEndPoint,
                sessionRequestEx1.ClientIp).Result;
            Assert.AreEqual(SessionErrorCode.SessionClosed, response.ErrorCode);
            Assert.AreEqual(60, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(30, response.AccessUsage?.ReceivedTraffic);

            // check restore
            var accessServer2 = TestHelper.CreateFileAccessServer(storagePath : storagePath);
            response = accessServer2.Session_Create(sessionRequestEx1).Result;
            Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);
            Assert.AreEqual(60, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(30, response.AccessUsage?.ReceivedTraffic);
        }
    }
}