using VpnHood.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VpnHood.Server.AccessServers;
using System.Runtime.CompilerServices;

namespace VpnHood.Test
{
    [TestClass]
    public class FileAccessServer_Test
    {
        [TestMethod]
        public void CRUD()
        {
            var tokenPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var store1 = new FileAccessServer(tokenPath);

            //add two tokens
            var accessItem1 = TestHelper.CreateDefaultAccessItem(443);
            accessItem1.Token.TokenId = Guid.NewGuid();
            var supportId1 = store1.AddAccessItem(accessItem1);
            var clientIdentity1 = new ClientIdentity() { TokenId = accessItem1.Token.TokenId };

            var accessItem2 = TestHelper.CreateDefaultAccessItem(443);
            accessItem2.Token.TokenId = Guid.NewGuid();
            var supportId2 = store1.AddAccessItem(accessItem2);
            var clientIdentity2 = new ClientIdentity() { TokenId = accessItem2.Token.TokenId };

            var accessItem3 = TestHelper.CreateDefaultAccessItem(443);
            accessItem3.Token.TokenId = Guid.NewGuid();
            var supportId3 = store1.AddAccessItem(accessItem3);
            var clientIdentity3 = new ClientIdentity() { TokenId = accessItem3.Token.TokenId };

            // ************
            // *** TEST ***: get all tokensId
            var tokenIds = store1.GetAllTokenIds();
            Assert.IsTrue(tokenIds.Any(x => x == accessItem1.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == accessItem2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == accessItem3.Token.TokenId));
            Assert.AreEqual(3, tokenIds.Length);


            // ************
            // *** TEST ***: token must be retreived with TokenId
            Assert.AreEqual(AccessStatusCode.Ok, store1.GetAccess(clientIdentity1).Result?.StatusCode, "access has not been retreived");

            // ************
            // *** TEST ***: token must be retreived with SupportId
            Assert.AreEqual(accessItem1.Token.TokenId, store1.TokenIdFromSupportId(supportId1));
            Assert.AreEqual(accessItem2.Token.TokenId, store1.TokenIdFromSupportId(supportId2));
            Assert.AreEqual(accessItem3.Token.TokenId, store1.TokenIdFromSupportId(supportId3));

            // ************
            // *** TEST ***: Removeing token
            store1.RemoveToken(accessItem1.Token.TokenId).Wait();
            tokenIds = store1.GetAllTokenIds();
            Assert.IsFalse(tokenIds.Any(x => x == accessItem1.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == accessItem2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == accessItem3.Token.TokenId));
            Assert.AreEqual(2, tokenIds.Length);
            Assert.IsNull(store1.GetAccess(clientIdentity1).Result, "access should not be exist");

            try
            {
                store1.TokenIdFromSupportId(supportId1);
                Assert.Fail("exception was expected");
            }
            catch (KeyNotFoundException)
            {
            }

            // ************
            // *** TEST ***: token must be retreived after reloading (last operation is remove)
            var store2 = new FileAccessServer(tokenPath);

            tokenIds = store2.GetAllTokenIds();
            Assert.IsTrue(tokenIds.Any(x => x == accessItem2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == accessItem3.Token.TokenId));
            Assert.AreEqual(2, tokenIds.Length);

            // ************
            // *** TEST ***: token must be retreived with TokenId
            Assert.AreEqual(AccessStatusCode.Ok, store2.GetAccess(clientIdentity2).Result?.StatusCode, "Access has not been retreived");

            // ************
            // *** TEST ***: token must be retreived with SupportId
            Assert.AreEqual(accessItem2.Token.TokenId, store2.TokenIdFromSupportId(supportId2));
            Assert.AreEqual(accessItem3.Token.TokenId, store2.TokenIdFromSupportId(supportId3));

            // ************
            // *** TEST ***: token must be retreived after reloading (last operation is add)
            var accessItem4 = TestHelper.CreateDefaultAccessItem(443);
            accessItem4.Token.TokenId = Guid.NewGuid();
            var supportId4 = store1.AddAccessItem(accessItem4);
            var store3 = new FileAccessServer(tokenPath);
            tokenIds = store3.GetAllTokenIds();
            Assert.AreEqual(3, tokenIds.Length);
            Assert.AreEqual(AccessStatusCode.Ok, store3.GetAccess(clientIdentity2).Result?.StatusCode, "access has not been retreived");
        }

        [TestMethod]
        public void AddUsage()
        {
            var tokenPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var store1 = new FileAccessServer(tokenPath);

            //add token
            var accessItem1 = TestHelper.CreateDefaultAccessItem(443);
            accessItem1.Token.TokenId = Guid.NewGuid();
            store1.AddAccessItem(accessItem1);

            // ************
            // *** TEST ***: access must be retreived by AddUsage
            var clientIdentity = new ClientIdentity() { TokenId = accessItem1.Token.TokenId };
            var access = store1.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity }).Result;
            Assert.IsNotNull(access, "access has not been retreived");

            // ************
            // *** TEST ***: add sent and receive bytes
            access = store1.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity, SentTrafficByteCount = 20, ReceivedTrafficByteCount = 10 }).Result;
            Assert.AreEqual(20, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);

            access = store1.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity, SentTrafficByteCount = 20, ReceivedTrafficByteCount = 10 }).Result;
            Assert.AreEqual(40, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);

            access = store1.GetAccess(clientIdentity).Result;
            Assert.AreEqual(40, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);

            // check restore
            var store2 = new FileAccessServer(tokenPath);
            access = store2.GetAccess(clientIdentity).Result;
            Assert.AreEqual(40, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);
        }

    }
}
