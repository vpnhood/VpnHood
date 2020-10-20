using VpnHood.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VpnHood.Server.TokenStores;

namespace VpnHood.Test
{
    [TestClass]
    public class FileTokenStore_Test
    {
        [TestMethod]
        public void CRUD()
        {
            var tokenPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
            var store1 = new FileTokenStore(tokenPath);

            //add two tokens
            var tokenInfo1 = TestHelper.CreateDefaultTokenInfo(443);
            tokenInfo1.Token.TokenId = Guid.NewGuid();
            var supportId1 = store1.AddToken(tokenInfo1);

            var tokenInfo2 = TestHelper.CreateDefaultTokenInfo(443);
            tokenInfo2.Token.TokenId = Guid.NewGuid();
            var supportId2 = store1.AddToken(tokenInfo2);

            var tokenInfo3 = TestHelper.CreateDefaultTokenInfo(443);
            tokenInfo3.Token.TokenId = Guid.NewGuid();
            var supportId3 = store1.AddToken(tokenInfo3);

            // ************
            // *** TEST ***: get all tokensId
            var tokenIds = store1.GetAllTokenIds();
            Assert.IsTrue(tokenIds.Any(x=>x== tokenInfo1.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x=>x== tokenInfo2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x=>x== tokenInfo3.Token.TokenId));
            Assert.AreEqual(3, tokenIds.Length);


            // ************
            // *** TEST ***: token must be retrieved with TokenId
            Assert.IsNotNull(store1.GetTokenInfo(tokenInfo1.Token.TokenId).Result.Token, "token has not been retrieved");
            Assert.IsNotNull(store1.GetTokenInfo(tokenInfo2.Token.TokenId).Result.Token, "token has not been retrieved");
            Assert.IsNotNull(store1.GetTokenInfo(tokenInfo3.Token.TokenId).Result.TokenUsage, "tokenUsage has not been retrieved");

            // ************
            // *** TEST ***: token must be retrieved with SupportId
            Assert.AreEqual(tokenInfo1.Token.TokenId, store1.TokenIdFromSupportId(supportId1));
            Assert.AreEqual(tokenInfo2.Token.TokenId, store1.TokenIdFromSupportId(supportId2));
            Assert.AreEqual(tokenInfo3.Token.TokenId, store1.TokenIdFromSupportId(supportId3));

            // ************
            // *** TEST ***: Removeing token
            store1.RemoveToken(tokenInfo1.Token.TokenId).Wait();
            tokenIds = store1.GetAllTokenIds();
            Assert.IsFalse(tokenIds.Any(x => x == tokenInfo1.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == tokenInfo2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == tokenInfo3.Token.TokenId));
            Assert.AreEqual(2, tokenIds.Length);
            Assert.IsNull(store1.GetTokenUsage(tokenInfo1.Token.TokenId).Result, "TokenUsage should not be exist");
            Assert.IsNull(store1.GetTokenInfo(tokenInfo1.Token.TokenId).Result, "TokenInfo should not be exist");

            try
            {
                store1.TokenIdFromSupportId(supportId1);
                Assert.Fail("exception was expected");
            }
            catch  (KeyNotFoundException)
            {
            }

            // ************
            // *** TEST ***: tokenUsage must be retrieved with SupportId

            // ************
            // *** TEST ***: token must be retrieved after reloading (last operation is remove)
            var store2 = new FileTokenStore(tokenPath);
            
            tokenIds = store2.GetAllTokenIds();
            Assert.IsTrue(tokenIds.Any(x => x == tokenInfo2.Token.TokenId));
            Assert.IsTrue(tokenIds.Any(x => x == tokenInfo3.Token.TokenId));
            Assert.AreEqual(2, tokenIds.Length);

            // ************
            // *** TEST ***: token must be retrieved with TokenId
            Assert.IsNotNull(store2.GetTokenInfo(tokenInfo2.Token.TokenId), "tokenInfo has not been retrieved");
            Assert.IsNotNull(store2.GetTokenInfo(tokenInfo2.Token.TokenId).Result.Token, "token has not been retrieved");
            Assert.IsNotNull(store2.GetTokenInfo(tokenInfo3.Token.TokenId).Result.TokenUsage, "tokenUsage has not been retrieved");

            // ************
            // *** TEST ***: token must be retrieved with SupportId
            Assert.AreEqual(tokenInfo2.Token.TokenId, store2.TokenIdFromSupportId(supportId2));
            Assert.AreEqual(tokenInfo3.Token.TokenId, store2.TokenIdFromSupportId(supportId3));

            // ************
            // *** TEST ***: token must be retrieved after reloading (last operation is add)
            var tokenInfo4 = TestHelper.CreateDefaultTokenInfo(443);
            tokenInfo4.Token.TokenId = Guid.NewGuid();
            var supportId4 = store1.AddToken(tokenInfo4);
            var store3 = new FileTokenStore(tokenPath);
            tokenIds = store3.GetAllTokenIds();
            Assert.AreEqual(3, tokenIds.Length);
            Assert.IsNotNull(store3.GetTokenInfo(tokenInfo4.Token.TokenId).Result, "token has not been retrieved");
        }
    }
}
