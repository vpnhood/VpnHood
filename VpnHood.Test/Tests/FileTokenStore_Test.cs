using VpnHood.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VpnHood.Server.AccessServers;
using System.Security.Cryptography.X509Certificates;
using System.Net;

namespace VpnHood.Test
{
    [TestClass]
    public class FileAccessServer_Test
    {
        [TestMethod]
        public void GetSslCertificateData()
        {
            var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var accessServer = new FileAccessServer(storagePath, "1");
            var cert1 = new X509Certificate2(accessServer.GetSslCertificateData("20.20.20.20").Result);
            var cert2 = new X509Certificate2(Path.Combine(accessServer.CertificatesFolderPath, "20.20.20.20.pfx"), "1", X509KeyStorageFlags.Exportable);
            Assert.AreEqual(cert1.Thumbprint, cert2.Thumbprint);
        }

        [TestMethod]
        public void CRUD()
        {
            var serverEndPoint = IPEndPoint.Parse("1.1.1.1:443");
            var storagePath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var accessServer1 = new FileAccessServer(storagePath);

            //add two tokens
            var accessItem1 = accessServer1.CreateAccessItem(serverEndPoint);
            var clientIdentity1 = new ClientIdentity() { TokenId = accessItem1.Token.TokenId };

            var accessItem2 = accessServer1.CreateAccessItem(serverEndPoint);
            var clientIdentity2 = new ClientIdentity() { TokenId = accessItem2.Token.TokenId };

            var accessItem3 = accessServer1.CreateAccessItem(serverEndPoint);
            var clientIdentity3 = new ClientIdentity() { TokenId = accessItem3.Token.TokenId };

            // ************
            // *** TEST ***: get all tokensId
            var tokenIds = accessServer1.GetAllTokenIds();
            Assert.IsTrue(tokenIds.Any(x => x == accessItem1.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == accessItem2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == accessItem3.Token.TokenId));
            Assert.AreEqual(3, tokenIds.Length);


            // ************
            // *** TEST ***: token must be retreived with TokenId
            Assert.AreEqual(AccessStatusCode.Ok, accessServer1.GetAccess(clientIdentity1).Result?.StatusCode, "access has not been retreived");

            // ************
            // *** TEST ***: token must be retreived with SupportId
            Assert.AreEqual(accessItem1.Token.TokenId, accessServer1.TokenIdFromSupportId(accessItem1.Token.SupportId));
            Assert.AreEqual(accessItem2.Token.TokenId, accessServer1.TokenIdFromSupportId(accessItem2.Token.SupportId));
            Assert.AreEqual(accessItem3.Token.TokenId, accessServer1.TokenIdFromSupportId(accessItem3.Token.SupportId));

            // ************
            // *** TEST ***: Removeing token
            accessServer1.RemoveToken(accessItem1.Token.TokenId).Wait();
            tokenIds = accessServer1.GetAllTokenIds();
            Assert.IsFalse(tokenIds.Any(x => x == accessItem1.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == accessItem2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == accessItem3.Token.TokenId));
            Assert.AreEqual(2, tokenIds.Length);
            Assert.IsNull(accessServer1.GetAccess(clientIdentity1).Result, "access should not be exist");

            try
            {
                accessServer1.TokenIdFromSupportId(accessItem1.Token.SupportId);
                Assert.Fail("exception was expected");
            }
            catch (KeyNotFoundException)
            {
            }

            // ************
            // *** TEST ***: token must be retreived after reloading (last operation is remove)
            var accessServer2 = new FileAccessServer(storagePath);

            tokenIds = accessServer2.GetAllTokenIds();
            Assert.IsTrue(tokenIds.Any(x => x == accessItem2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == accessItem3.Token.TokenId));
            Assert.AreEqual(2, tokenIds.Length);

            // ************
            // *** TEST ***: token must be retreived with TokenId
            Assert.AreEqual(AccessStatusCode.Ok, accessServer2.GetAccess(clientIdentity2).Result?.StatusCode, "Access has not been retreived");

            // ************
            // *** TEST ***: token must be retreived with SupportId
            Assert.AreEqual(accessItem2.Token.TokenId, accessServer2.TokenIdFromSupportId(accessItem2.Token.SupportId));
            Assert.AreEqual(accessItem3.Token.TokenId, accessServer2.TokenIdFromSupportId(accessItem3.Token.SupportId));

            // ************
            // *** TEST ***: token must be retreived after reloading (last operation is add)
            var accessItem4 = accessServer1.CreateAccessItem(serverEndPoint);
            
            var accessServer3 = new FileAccessServer(storagePath);
            tokenIds = accessServer3.GetAllTokenIds();
            Assert.AreEqual(3, tokenIds.Length);
            Assert.AreEqual(AccessStatusCode.Ok, accessServer3.GetAccess(clientIdentity2).Result?.StatusCode, "access has not been retreived");
        }

        [TestMethod]
        public void AddUsage()
        {
            var tokenPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var accessServer1 = new FileAccessServer(tokenPath);
            var serverEndPoint = IPEndPoint.Parse("1.1.1.1:443");

            //add token
            var accessItem1 = accessServer1.CreateAccessItem(serverEndPoint);

            // ************
            // *** TEST ***: access must be retreived by AddUsage
            var clientIdentity = new ClientIdentity() { TokenId = accessItem1.Token.TokenId };
            var access = accessServer1.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity }).Result;
            Assert.IsNotNull(access, "access has not been retreived");

            // ************
            // *** TEST ***: add sent and receive bytes
            access = accessServer1.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity, SentTrafficByteCount = 20, ReceivedTrafficByteCount = 10 }).Result;
            Assert.AreEqual(20, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);

            access = accessServer1.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity, SentTrafficByteCount = 20, ReceivedTrafficByteCount = 10 }).Result;
            Assert.AreEqual(40, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);

            access = accessServer1.GetAccess(clientIdentity).Result;
            Assert.AreEqual(40, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);

            // check restore
            var accessServer2 = new FileAccessServer(tokenPath);
            access = accessServer2.GetAccess(clientIdentity).Result;
            Assert.AreEqual(40, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);
        }

    }
}
