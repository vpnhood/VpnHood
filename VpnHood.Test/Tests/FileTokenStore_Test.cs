using VpnHood.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VpnHood.Server.TokenStores;
using System.Runtime.CompilerServices;

namespace VpnHood.Test
{
    [TestClass]
    public class FileTokenStore_Test
    {
        [TestMethod]
        public void CRUD()
        {
            var tokenPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var store1 = new FileClientStore(tokenPath);

            //add two tokens
            var clientInfo1 = TestHelper.CreateDefaultClientInfo(443);
            clientInfo1.Token.TokenId = Guid.NewGuid();
            var supportId1 = store1.AddToken(clientInfo1);

            var clientInfo2 = TestHelper.CreateDefaultClientInfo(443);
            clientInfo2.Token.TokenId = Guid.NewGuid();
            var supportId2 = store1.AddToken(clientInfo2);

            var clientInfo3 = TestHelper.CreateDefaultClientInfo(443);
            clientInfo3.Token.TokenId = Guid.NewGuid();
            var supportId3 = store1.AddToken(clientInfo3);

            // ************
            // *** TEST ***: get all tokensId
            var tokenIds = store1.GetAllTokenIds();
            Assert.IsTrue(tokenIds.Any(x => x == clientInfo1.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == clientInfo2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == clientInfo3.Token.TokenId));
            Assert.AreEqual(3, tokenIds.Length);


            // ************
            // *** TEST ***: token must be retrieved with TokenId
            Assert.IsNotNull(store1.GetClientInfo(clientInfo1.Token.TokenId, true).Result.Token, "Token has not been retrieved");
            Assert.IsNull(store1.GetClientInfo(clientInfo2.Token.TokenId, false).Result.Token, "Token should not be retrieved");
            Assert.IsNotNull(store1.GetClientInfo(clientInfo3.Token.TokenId, false).Result.ClientUsage, "ClientUsage has not been retrieved");
            Assert.IsNotNull(store1.GetClientInfo(clientInfo3.Token.TokenId, false).Result.TokenSettings, "TokenSettings has not been retrieved");

            // ************
            // *** TEST ***: token must be retrieved with SupportId
            Assert.AreEqual(clientInfo1.Token.TokenId, store1.TokenIdFromSupportId(supportId1));
            Assert.AreEqual(clientInfo2.Token.TokenId, store1.TokenIdFromSupportId(supportId2));
            Assert.AreEqual(clientInfo3.Token.TokenId, store1.TokenIdFromSupportId(supportId3));

            // ************
            // *** TEST ***: Removeing token
            store1.RemoveToken(clientInfo1.Token.TokenId).Wait();
            tokenIds = store1.GetAllTokenIds();
            Assert.IsFalse(tokenIds.Any(x => x == clientInfo1.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == clientInfo2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == clientInfo3.Token.TokenId));
            Assert.AreEqual(2, tokenIds.Length);
            Assert.IsNull(store1.GetClientInfo(clientInfo1.Token.TokenId, false).Result, "ClientInfo should not be exist");

            try
            {
                store1.TokenIdFromSupportId(supportId1);
                Assert.Fail("exception was expected");
            }
            catch (KeyNotFoundException)
            {
            }

            // ************
            // *** TEST ***: token must be retrieved after reloading (last operation is remove)
            var store2 = new FileClientStore(tokenPath);

            tokenIds = store2.GetAllTokenIds();
            Assert.IsTrue(tokenIds.Any(x => x == clientInfo2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == clientInfo3.Token.TokenId));
            Assert.AreEqual(2, tokenIds.Length);

            // ************
            // *** TEST ***: token must be retrieved with TokenId
            Assert.IsNotNull(store2.GetClientInfo(clientInfo2.Token.TokenId, withToken: true).Result, "GetClientInfo has not been retrieved");
            Assert.IsNotNull(store2.GetClientInfo(clientInfo2.Token.TokenId, withToken: true).Result.Token, "Token has not been retrieved");
            Assert.IsNull(store2.GetClientInfo(clientInfo2.Token.TokenId, withToken: false).Result.Token, "Token should not been retrieved");
            Assert.IsNotNull(store2.GetClientInfo(clientInfo3.Token.TokenId, withToken: false).Result.ClientUsage, "ClientUsage has not been retrieved");

            // ************
            // *** TEST ***: token must be retrieved with SupportId
            Assert.AreEqual(clientInfo2.Token.TokenId, store2.TokenIdFromSupportId(supportId2));
            Assert.AreEqual(clientInfo3.Token.TokenId, store2.TokenIdFromSupportId(supportId3));

            // ************
            // *** TEST ***: token must be retrieved after reloading (last operation is add)
            var clientInfo4 = TestHelper.CreateDefaultClientInfo(443);
            clientInfo4.Token.TokenId = Guid.NewGuid();
            var supportId4 = store1.AddToken(clientInfo4);
            var store3 = new FileClientStore(tokenPath);
            tokenIds = store3.GetAllTokenIds();
            Assert.AreEqual(3, tokenIds.Length);
            Assert.IsNotNull(store3.GetClientInfo(clientInfo2.Token.TokenId, withToken: true).Result, "clientInfo has not been retrieved");
            Assert.IsNotNull(store3.GetClientInfo(clientInfo2.Token.TokenId, withToken: true).Result.Token, "Token has not been retrieved");
            Assert.IsNull(store3.GetClientInfo(clientInfo2.Token.TokenId, withToken: false).Result.Token, "Token should not been retrieved");
            Assert.IsNotNull(store3.GetClientInfo(clientInfo3.Token.TokenId, withToken: false).Result.ClientUsage, "ClientUsage has not been retrieved");
            Assert.IsNotNull(store3.GetClientInfo(clientInfo3.Token.TokenId, withToken: false).Result.TokenSettings, "TokenSettings has not been retrieved");
        }

        [TestMethod]
        public void AddClientUsage()
        {
            var tokenPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var store1 = new FileClientStore(tokenPath);

            //add two tokens
            var clientInfo1 = TestHelper.CreateDefaultClientInfo(443);
            clientInfo1.Token.TokenId = Guid.NewGuid();

            // ************
            // *** TEST ***: clientInfo must be retrieved by setting clientUsage to null
            var clientIdentity = new ClientIdentity() { TokenId = clientInfo1.Token.TokenId };
            var clientInfo = store1.AddClientUsage(clientIdentity, null, true).Result;
            Assert.AreEqual(clientInfo1.Token.TokenId, clientInfo?.Token?.TokenId, "Token has not been retrieved");
            clientInfo = store1.AddClientUsage(clientIdentity, null, false).Result;
            Assert.IsNull(clientInfo?.Token, "Token must be null when withToke is false");

            // ************
            // *** TEST ***: add sent and receive bytes
            clientInfo = store1.AddClientUsage(clientIdentity, new ClientUsage() { ReceivedByteCount = 10, SentByteCount = 20 }, true).Result;
            Assert.AreEqual(10, clientInfo.ClientUsage.ReceivedByteCount);
            Assert.AreEqual(20, clientInfo.ClientUsage.SentByteCount);

            clientInfo = store1.AddClientUsage(clientIdentity, new ClientUsage() { ReceivedByteCount = 10, SentByteCount = 20 }, false).Result;
            Assert.AreEqual(20, clientInfo.ClientUsage.ReceivedByteCount);
            Assert.AreEqual(40, clientInfo.ClientUsage.SentByteCount);

            clientInfo = store1.GetClientInfo(clientIdentity, false).Result;
            Assert.AreEqual(20, clientInfo.ClientUsage.ReceivedByteCount);
            Assert.AreEqual(40, clientInfo.ClientUsage.SentByteCount);

            // check restore
            var store2 = new FileClientStore(tokenPath);
            clientInfo = store2.GetClientInfo(clientIdentity, false).Result;
            Assert.AreEqual(20, clientInfo.ClientUsage.ReceivedByteCount);
            Assert.AreEqual(40, clientInfo.ClientUsage.SentByteCount);
        }

    }
}
