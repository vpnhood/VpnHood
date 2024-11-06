using GrayMint.Common.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;
using VpnHood.Common.Tokens;
using VpnHood.Common.Utils;
using AdRequirement = VpnHood.AccessServer.Api.AdRequirement;
using ClientPolicy = VpnHood.AccessServer.Api.ClientPolicy;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AdTest
{
    [TestMethod]
    public async Task Create_session_with_flexible_ad()
    {
        using var farm = await ServerFarmDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(adRequirement: AdRequirement.Flexible);
        var sessionDom = await accessTokenDom.CreateSession();
        Assert.AreEqual(VpnHood.Common.Messaging.AdRequirement.Flexible, sessionDom.SessionResponseEx.AdRequirement);
    }

    [TestMethod]
    public async Task Pending_ad_session_must_close_after_timeout()
    {
        using var farm = await ServerFarmDom.Create();
        farm.TestApp.AgentTestApp.AgentOptions.AdRewardPendingTimeout = TimeSpan.FromSeconds(1);

        // create token
        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", PremiumByRewardAd = 9 } };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);

        // create session
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.PremiumByAdReward);
        Assert.IsTrue(sessionDom.SessionResponseEx.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
        Assert.AreEqual(AdRequirement.Required.ToString(), sessionDom.SessionResponseEx.AdRequirement.ToString());

        await TestUtil.AssertEqualsWait(SessionErrorCode.AdError, async ()=> {
            var sessionResponse  = await sessionDom.AddUsage();
            return sessionResponse.ErrorCode;
        });
    }

    [TestMethod]
    public async Task Pending_ad_session_must_extend_by_ad()
    {
        using var farm = await ServerFarmDom.Create();
        farm.TestApp.AgentTestApp.AgentOptions.AdRewardPendingTimeout = TimeSpan.FromSeconds(1);

        // create token
        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", PremiumByRewardAd = 9 } };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);

        // create session
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.PremiumByAdReward);
        Assert.IsTrue(sessionDom.SessionResponseEx.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
        Assert.AreEqual(AdRequirement.Required.ToString(), sessionDom.SessionResponseEx.AdRequirement.ToString());

        // send ad
        var adData = Guid.NewGuid().ToString();
        farm.TestApp.AgentTestApp.CacheService.Ad_AddRewardData(farm.ProjectId, adData);
        await sessionDom.AddUsage(adData: adData, traffic: new Traffic());

        // check session
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        var sessionResponse = await sessionDom.AddUsage(traffic: new Traffic());
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponse.ErrorCode);

        // don't extend session with the old code
        sessionResponse = await sessionDom.AddUsage(adData: adData, traffic: new Traffic());
        Assert.IsFalse(sessionResponse.ExpirationTime > DateTime.UtcNow.AddMinutes(10));

        // extend session with new code
        adData = Guid.NewGuid().ToString();
        farm.TestApp.AgentTestApp.CacheService.Ad_AddRewardData(farm.ProjectId, adData);
        sessionResponse = await sessionDom.AddUsage(adData: adData, traffic: new Traffic());
        Assert.IsTrue(sessionResponse.ExpirationTime > DateTime.UtcNow.AddMinutes(10));
    }

    [TestMethod]
    public async Task Create_second_session_should_be_auto_rewarded()
    {
        using var farm = await ServerFarmDom.Create();

        // create token
        var clientPolicies = new [] { new ClientPolicy { ClientCountry = "*", PremiumByRewardAd = 5} };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);

        // check access token
        var clientId = Guid.NewGuid().ToString();

        // create session
        var sessionDom = await accessTokenDom.CreateSession(clientId: clientId, planId: ConnectPlanId.PremiumByAdReward);
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