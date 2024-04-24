using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AdTest
{
    [TestMethod]
    public async Task Create_session_with_temporary_expiration()
    {
        var farm1 = await ServerFarmDom.Create();
        
        // create token
        var accessTokenDom = await farm1.CreateAccessToken(new AccessTokenCreateParams
        {
            ServerFarmId = farm1.ServerFarmId,
            AccessTokenName = "tokenName1",
            Url = "https://foo.com/accessKey1",
            IsAdRequired = true,
            IsEnabled = true
        });

        // check access token
        var token = await accessTokenDom.GetToken();
        Assert.IsTrue(token.IsAdRequired);

        // create session
        var sessionDom = await accessTokenDom.CreateSession();
        Assert.IsTrue(sessionDom.SessionResponseEx.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
        Assert.IsTrue(sessionDom.SessionResponseEx.IsAdRequired);

        // extend session
        var sessionResponse = await sessionDom.AddUsage();
        Assert.IsTrue(sessionResponse.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
        var adData = Guid.NewGuid().ToString();
        farm1.TestApp.AgentTestApp.CacheService.RewardAd(farm1.ProjectId, adData);
        sessionResponse = await sessionDom.AddUsage(new Traffic(), adData);
        Assert.IsNull(sessionResponse.AccessUsage?.ExpirationTime);

        //wrong or used ad should close the session
        sessionResponse = await sessionDom.AddUsage(new Traffic(), adData);
        Assert.AreEqual(SessionErrorCode.AdError, sessionResponse.ErrorCode);
    }
}