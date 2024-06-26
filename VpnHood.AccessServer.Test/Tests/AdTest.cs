using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;
using AdRequirement = VpnHood.AccessServer.Api.AdRequirement;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AdTest
{
    [TestMethod]
    public async Task Create_session_with_temporary_expiration()
    {
        var farm = await ServerFarmDom.Create();
        
        // create token
        var accessTokenDom = await farm.CreateAccessToken(new AccessTokenCreateParams
        {
            ServerFarmId = farm.ServerFarmId,
            AccessTokenName = Guid.NewGuid().ToString(),
            Description = Guid.NewGuid().ToString(),
            AdRequirement = AdRequirement.Required,
            IsEnabled = true
        });

        // create session
        var sessionDom = await accessTokenDom.CreateSession();
        Assert.IsTrue(sessionDom.SessionResponseEx.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
        Assert.AreEqual(AdRequirement.Required.ToString(), sessionDom.SessionResponseEx.AdRequirement.ToString());

        // extend session
        var sessionResponse = await sessionDom.AddUsage();
        Assert.IsTrue(sessionResponse.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
        var adData = Guid.NewGuid().ToString();
        farm.TestApp.AgentTestApp.CacheService.Ad_AddRewardData(farm.ProjectId, adData);
        sessionResponse = await sessionDom.AddUsage(new Traffic(), adData);
        Assert.IsNull(sessionResponse.AccessUsage?.ExpirationTime);

        //wrong or used ad should close the session
        sessionResponse = await sessionDom.AddUsage(new Traffic(), adData);
        Assert.AreEqual(SessionErrorCode.AdError, sessionResponse.ErrorCode);
    }

    [TestMethod]
    public async Task Create_second_session_should_be_auto_rewarded()
    {
        var farm = await ServerFarmDom.Create();

        // create token
        var accessTokenDom = await farm.CreateAccessToken(new AccessTokenCreateParams
        {
            ServerFarmId = farm.ServerFarmId,
            AccessTokenName = Guid.NewGuid().ToString(),
            AdRequirement =  AdRequirement.Required,
            IsEnabled = true,
            Description = Guid.NewGuid().ToString()
        });

        // check access token
        var clientId = Guid.NewGuid();

        // create session
        var sessionDom = await accessTokenDom.CreateSession(clientId: clientId);
        Assert.IsTrue(sessionDom.SessionResponseEx.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
        Assert.AreEqual(AdRequirement.Required.ToString(), sessionDom.SessionResponseEx.AdRequirement.ToString());

        // extend session
        var sessionResponse = await sessionDom.AddUsage();
        Assert.IsTrue(sessionResponse.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
        var adData = Guid.NewGuid().ToString();
        farm.TestApp.AgentTestApp.CacheService.Ad_AddRewardData(farm.ProjectId, adData);
        sessionResponse = await sessionDom.AddUsage(new Traffic(), adData);
        Assert.IsNull(sessionResponse.AccessUsage?.ExpirationTime);

        // next session should be auto rewarded
        var sessionDom2 = await accessTokenDom.CreateSession(clientId: clientId);
        Assert.IsNull(sessionDom2.SessionResponseEx.AccessUsage?.ExpirationTime);
    }

}