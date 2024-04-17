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
            IsAdRequired = true
        });


        // create session
        var sessionDom = await accessTokenDom.CreateSession();
        Assert.IsTrue(sessionDom.SessionResponseEx.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(10));

        // extend session
        var sessionResponse = await sessionDom.AddUsage();
        Assert.IsTrue(sessionResponse.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
        var adData = Guid.NewGuid().ToString();
        farm1.TestApp.AgentTestApp.CacheService.AddAd(farm1.ProjectId, adData);
        sessionResponse = await sessionDom.AddUsage(new Traffic(), adData);
        Assert.IsNull(sessionResponse.AccessUsage?.ExpirationTime);

        //wrong or used ad should close the session
        sessionResponse = await sessionDom.AddUsage(new Traffic(), adData);
        Assert.AreEqual(SessionErrorCode.AdError, sessionResponse.ErrorCode);
    }

    [TestMethod]
    public async Task Create_session_with_ad_reward()
    {
        var farm1 = await ServerFarmDom.Create();
        var adData = Guid.NewGuid().ToString();
        farm1.TestApp.AgentTestApp.CacheService.AddAd(farm1.ProjectId, adData);

        // create token
        var accessTokenDom = await farm1.CreateAccessToken(new AccessTokenCreateParams
        {
            ServerFarmId = farm1.ServerFarmId,
            AccessTokenName = "tokenName1",
            Url = "https://foo.com/accessKey1",
            IsAdRequired = true
        });


        // create session
        var sessionDom = await accessTokenDom.CreateSession(adData: adData);
        Assert.IsNull(sessionDom.SessionResponseEx.AccessUsage?.ExpirationTime);
    }
}