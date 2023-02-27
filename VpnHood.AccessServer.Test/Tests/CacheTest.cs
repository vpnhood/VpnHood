using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Test.Dom;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class CacheTest
{
    [TestMethod]
    public async Task Auto_Flush()
    {
        var testInit = await TestInit.Create(appSettings: new Dictionary<string, string?>
        {
            ["App:SaveCacheInterval"] = "00:00:00.100"
        });

        var farmDom = await ServerFarmDom.Create(testInit);
        var accessTokenDom = await farmDom.CreateAccessToken(true);
        var sessionDom = await accessTokenDom.CreateSession();
        await sessionDom.CloseSession();
        await Task.Delay(1000);

        Assert.IsTrue(
            await testInit.VhContext.Sessions.AnyAsync(x =>
                x.SessionId == sessionDom.SessionId && x.EndTime != null),
            "Session has not been synced yet.");
    }

    [TestMethod]
    public async Task Save_Cache_after_deleting_a_token()
    {
        var farmDom = await ServerFarmDom.Create();

        // session1
        var accessTokenDom1 = await farmDom.CreateAccessToken();
        var sessionDom1 = await accessTokenDom1.CreateSession();
        await sessionDom1.AddUsage();
        await accessTokenDom1.TestInit.AccessTokensClient.DeleteAsync(farmDom.ProjectId, accessTokenDom1.AccessTokenId);

        // session2
        var accessTokenDom2 = await farmDom.CreateAccessToken();
        var sessionDom2 = await accessTokenDom2.CreateSession();
        await sessionDom2.AddUsage();

        // session 1 & 2 must exists
        await farmDom.TestInit.FlushCache();

        Assert.IsTrue(await farmDom.TestInit.VhContext.Accesses.AnyAsync(x=>x.AccessTokenId== accessTokenDom1.AccessTokenId));
    }
}
