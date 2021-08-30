using VpnHood.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VpnHood.Server.AccessServers;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common;
using VpnHood.Server.Messaging;

namespace VpnHood.Test
{
    [TestClass]
    public class Test_FileAccessServer
    {
        [TestInitialize]
        public void Init()
        {
            VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
            VhLogger.IsDiagnoseMode = true;
        }

        [TestMethod]
        public void GetSslCertificateData()
        {
            var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var fileAccessServer = new FileAccessServer(storagePath, "1");

            // Create accessServer
            using TestEmbedIoAccessServer testRestAccessServer = new(fileAccessServer);
            var accessServer = new RestAccessServer(testRestAccessServer.BaseUri, "Bearer xxx", Guid.Empty);
            accessServer.Server_Subscribe(new ServerInfo(Version.Parse("1.1.1")) { MachineName = "TestMachine" }).Wait();

            // ************
            // *** TEST ***: default cert must be used when there is no InternalEndPoint
            fileAccessServer.AccessItem_Create(IPEndPoint.Parse("1.1.1.1:443"));
            var cert1 = new X509Certificate2(accessServer.GetSslCertificateData(IPEndPoint.Parse("2.2.2.2:443")).Result);
            Assert.AreEqual(cert1.Thumbprint, fileAccessServer.DefaultCert.Thumbprint);

            // ************
            // *** TEST ***: default cert should not be used when there is InternalEndPoint
            fileAccessServer.AccessItem_Create(IPEndPoint.Parse("1.1.1.1:443"), IPEndPoint.Parse("2.2.2.2:443"));
            cert1 = new X509Certificate2(accessServer.GetSslCertificateData(IPEndPoint.Parse("2.2.2.2:443")).Result);
            Assert.AreNotEqual(cert1.Thumbprint, fileAccessServer.DefaultCert.Thumbprint);
        }

        private static SessionRequestEx CreateSessionRequestEx(FileAccessServer.AccessItem accessItem, Guid clientId)
            => new(accessItem.Token.TokenId,
                   new ClientInfo { ClientId = clientId },
                   hostEndPoint: accessItem.Token.HostEndPoint!,
                   encryptedClientId: Util.EncryptClientId(clientId, accessItem.Token.Secret));

        [TestMethod]
        public void CRUD()
        {
            var hostEndPoint = IPEndPoint.Parse("1.1.1.1:443");
            var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var accessServer1 = new FileAccessServer(storagePath);
            var serverId = Guid.NewGuid();

            //add two tokens
            var accessItem1 = accessServer1.AccessItem_Create(hostEndPoint);
            var sessionRequestEx1 = CreateSessionRequestEx(accessItem1, Guid.NewGuid());

            var accessItem2 = accessServer1.AccessItem_Create(hostEndPoint);
            var sessionRequestEx2 = CreateSessionRequestEx(accessItem2, Guid.NewGuid());

            var accessItem3 = accessServer1.AccessItem_Create(hostEndPoint);
            var clientInfo3 = new ClientInfo { };
            var sessionRequestEx3 = CreateSessionRequestEx(accessItem3, Guid.NewGuid());

            // ************
            // *** TEST ***: get all tokensId
            var accessItems = accessServer1.AccessItem_LoadAll();
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem1.Token.TokenId));
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem2.Token.TokenId));
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem3.Token.TokenId));
            Assert.AreEqual(3, accessItems.Length);


            // ************
            // *** TEST ***: token must be retreived with TokenId
            Assert.AreEqual(SessionErrorCode.Ok, accessServer1.Session_Create(sessionRequestEx1).Result?.ErrorCode, "access has not been retreived");

            // ************
            // *** TEST ***: Removeing token
            accessServer1.AccessItem_Delete(accessItem1.Token.TokenId).Wait();
            accessItems = accessServer1.AccessItem_LoadAll();
            Assert.IsFalse(accessItems.Any(x => x.Token.TokenId == accessItem1.Token.TokenId));
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem2.Token.TokenId));
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem3.Token.TokenId));
            Assert.AreEqual(2, accessItems.Length);
            Assert.AreEqual(accessServer1.Session_Create(sessionRequestEx1).Result.ErrorCode, SessionErrorCode.GeneralError);

            // ************
            // *** TEST ***: token must be retreived by new instance after reloading (last operation is remove)
            var accessServer2 = new FileAccessServer(storagePath);

            accessItems = accessServer2.AccessItem_LoadAll();
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem2.Token.TokenId));
            Assert.IsTrue(accessItems.Any(x => x.Token.TokenId == accessItem3.Token.TokenId));
            Assert.AreEqual(2, accessItems.Length);

            // ************
            // *** TEST ***: token must be retreived with TokenId
            Assert.AreEqual(SessionErrorCode.Ok, accessServer2.Session_Create(sessionRequestEx2).Result?.ErrorCode, "Access has not been retreived");

            // ************
            // *** TEST ***: token must be retreived after reloading (last operation is add)
            var accessItem4 = accessServer1.AccessItem_Create(hostEndPoint);

            var accessServer3 = new FileAccessServer(storagePath);
            accessItems = accessServer3.AccessItem_LoadAll();
            Assert.AreEqual(3, accessItems.Length);
            Assert.AreEqual(SessionErrorCode.Ok, accessServer3.Session_Create(sessionRequestEx2).Result?.ErrorCode, "access has not been retreived");
        }

        [TestMethod]
        public void AddUsage()
        {
            var tokenPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var accessServer1 = new FileAccessServer(tokenPath);
            var hostEndPoint = IPEndPoint.Parse("1.1.1.1:443");

            //add token
            var accessItem1 = accessServer1.AccessItem_Create(hostEndPoint);
            var sessionRequestEx1 = CreateSessionRequestEx(accessItem1, Guid.NewGuid());

            // create a session
            var sessionResponse = accessServer1.Session_Create(sessionRequestEx1).Result;
            Assert.IsNotNull(sessionResponse, "access has not been retreived");

            // ************
            // *** TEST ***: add sent and receive bytes
            var response = accessServer1.Session_AddUsage(sessionResponse.SessionId, false, new UsageInfo { SentTraffic = 20, ReceivedTraffic = 10 }).Result;
            Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
            Assert.AreEqual(20, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, response.AccessUsage?.ReceivedTraffic);

            response = accessServer1.Session_AddUsage(sessionResponse.SessionId, false, new UsageInfo { SentTraffic = 20, ReceivedTraffic = 10 }).Result;
            Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
            Assert.AreEqual(40, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(20, response.AccessUsage?.ReceivedTraffic);

            response = accessServer1.Session_Get(sessionResponse.SessionId, sessionRequestEx1.HostEndPoint, sessionRequestEx1.ClientIp).Result;
            Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode, response.ErrorMessage);
            Assert.AreEqual(40, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(20, response.AccessUsage?.ReceivedTraffic);

            // close session
            response = accessServer1.Session_AddUsage(sessionResponse.SessionId, true, new UsageInfo { SentTraffic = 20, ReceivedTraffic = 10 }).Result;
            Assert.AreEqual(SessionErrorCode.SessionClosed, response.ErrorCode, response.ErrorMessage);
            Assert.AreEqual(60, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(30, response.AccessUsage?.ReceivedTraffic);

            // check is session closed
            response = accessServer1.Session_Get(sessionResponse.SessionId, sessionRequestEx1.HostEndPoint, sessionRequestEx1.ClientIp).Result;
            Assert.AreEqual(SessionErrorCode.SessionClosed, response.ErrorCode);
            Assert.AreEqual(60, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(30, response.AccessUsage?.ReceivedTraffic);

            // check restore
            var accessServer2 = new FileAccessServer(tokenPath);
            response = accessServer2.Session_Create(sessionRequestEx1).Result;
            Assert.AreEqual(SessionErrorCode.Ok, response.ErrorCode);
            Assert.AreEqual(60, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(30, response.AccessUsage?.ReceivedTraffic);
        }
    }
}
