using GrayMint.Common.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;
using VpnHood.Common.Tokens;
using VpnHood.Server.Access.Messaging;
using AdRequirement = VpnHood.AccessServer.Api.AdRequirement;
using ClientPolicy = VpnHood.AccessServer.Api.ClientPolicy;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AdTest
{
    [TestMethod]
    public async Task NormalAd_session_legacy()
    {
        using var farm = await ServerFarmDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(adRequirement: AdRequirement.Flexible);
        var sessionDom = await accessTokenDom.CreateSession();
        Assert.AreEqual(VpnHood.Common.Messaging.AdRequirement.Flexible, sessionDom.SessionResponseEx.AdRequirement);
    }

    [TestMethod]
    public async Task NormalAd_session()
    {
        using var farm = await ServerFarmDom.Create();
        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", Normal = 10} };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.Normal);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);
        Assert.IsTrue(sessionDom.SessionResponseEx.ExpirationTime > DateTime.UtcNow + TimeSpan.FromMinutes(8));
    }


    [TestMethod]
    public async Task NormalAd_unlimited_session_if_normal_is_zero()
    {
        using var farm = await ServerFarmDom.Create();
        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", Normal = 0 } };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.Normal);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);
        Assert.IsNull(sessionDom.SessionResponseEx.ExpirationTime);
    }

    [TestMethod]
    public async Task NormalAd_failed_if_not_set()
    {
        using var farm = await ServerFarmDom.Create();
        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", Normal = null} };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.Normal, throwError: false);
        Assert.AreEqual(SessionErrorCode.AccessError, sessionDom.SessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task NormalAd_premium_ok_even_if_normal_is_not_set()
    {
        using var farm = await ServerFarmDom.Create();
        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", Normal = null } };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: false, clientPolicies: clientPolicies);
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.Normal);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);
        Assert.IsNull(sessionDom.SessionResponseEx.ExpirationTime);
    }


    [TestMethod]
    public async Task RewardAd_pending_ad_session_must_close_after_timeout()
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
    public async Task RewardAd_pending_ad_session_must_extend_by_ad()
    {
        using var farm = await ServerFarmDom.Create();
        farm.TestApp.AgentTestApp.AgentOptions.AdRewardPendingTimeout = TimeSpan.FromSeconds(1);
        farm.TestApp.AgentTestApp.AgentOptions.AdRewardRetryCount = 0; // we don't to retry for ad provider

        // create token
        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", PremiumByRewardAd = 9 } };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);

        // create session
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.PremiumByAdReward);
        var totalMinutes = sessionDom.SessionResponseEx.ExpirationTime - DateTime.UtcNow;
        Assert.IsTrue(totalMinutes?.TotalMinutes < 10);
        Assert.AreEqual(AdRequirement.Required.ToString(), sessionDom.SessionResponseEx.AdRequirement.ToString());

        // send ad to remove pending
        var adData = Guid.NewGuid().ToString();
        farm.TestApp.AgentTestApp.CacheService.Ad_AddRewardData(farm.ProjectId, adData);
        var sessionResponse = await sessionDom.AddUsage(adData: adData, traffic: new Traffic());
        totalMinutes = sessionResponse.ExpirationTime - DateTime.UtcNow;
        Assert.IsTrue(totalMinutes?.TotalMinutes is < 10);

        // check session
        sessionResponse = await sessionDom.AddUsage(traffic: new Traffic());
        totalMinutes = sessionResponse.ExpirationTime - DateTime.UtcNow;
        Assert.IsTrue(totalMinutes?.TotalMinutes is < 10);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponse.ErrorCode);

        // don't extend session with the old code
        sessionResponse = await sessionDom.AddUsage(adData: adData, traffic: new Traffic());
        totalMinutes = sessionResponse.ExpirationTime - DateTime.UtcNow;
        Assert.IsTrue(totalMinutes?.TotalMinutes is <10);

        // extend session with new code
        adData = Guid.NewGuid().ToString();
        farm.TestApp.AgentTestApp.CacheService.Ad_AddRewardData(farm.ProjectId, adData);
        sessionResponse = await sessionDom.AddUsage(adData: adData, traffic: new Traffic());
        totalMinutes = sessionResponse.ExpirationTime - DateTime.UtcNow;
        Assert.IsTrue(totalMinutes?.TotalMinutes is > 10 and < 20);
    }

    [TestMethod]
    public async Task RewardAd_second_session_must_be_auto_rewarded()
    {
        using var farm = await ServerFarmDom.Create();

        // create token
        var clientPolicies = new [] { new ClientPolicy { ClientCountry = "*", PremiumByRewardAd = 10} };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);

        // check access token
        var clientId = Guid.NewGuid().ToString();

        // create session
        var sessionDom = await accessTokenDom.CreateSession(clientId: clientId, planId: ConnectPlanId.PremiumByAdReward);
        Assert.IsTrue(sessionDom.SessionResponseEx.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(11));
        Assert.AreEqual(AdRequirement.Required.ToString(), sessionDom.SessionResponseEx.AdRequirement.ToString());

        // send ad
        var adData = Guid.NewGuid().ToString();
        farm.TestApp.AgentTestApp.CacheService.Ad_AddRewardData(farm.ProjectId, adData);
        var sessionResponse = await sessionDom.AddUsage(new Traffic(), adData);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponse.ErrorCode);

        // next session should be auto rewarded
        sessionDom = await accessTokenDom.CreateSession(clientId: clientId, planId: ConnectPlanId.PremiumByAdReward);
        Assert.IsTrue(sessionDom.SessionResponseEx.AccessUsage?.ExpirationTime < DateTime.UtcNow.AddMinutes(11));
        Assert.AreEqual(AdRequirement.None.ToString(), sessionDom.SessionResponseEx.AdRequirement.ToString());
    }

    [TestMethod]
    public async Task RewardAd_unlimited_session_if_normal_is_zero()
    {
        using var farm = await ServerFarmDom.Create();
        farm.TestApp.AgentTestApp.AgentOptions.AdRewardPendingTimeout = TimeSpan.FromSeconds(1);
        farm.TestApp.AgentTestApp.AgentOptions.AdRewardRetryCount = 0; // we don't to retry for ad provider

        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", PremiumByRewardAd = 0 } };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.PremiumByAdReward);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);
        Assert.IsNull(sessionDom.SessionResponseEx.ExpirationTime);

        // wait for AddUsage get failed
        farm.TestApp.Logger.LogInformation("TEST: Waiting until reward pending times out.");
        await TestUtil.AssertEqualsWait(SessionErrorCode.AdError, async () => {
            var sessionResponse = await sessionDom.AddUsage();
            return sessionResponse.ErrorCode;
        });
    }

    [TestMethod]
    public async Task RewardAd_failed_if_not_set()
    {
        using var farm = await ServerFarmDom.Create();
        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", PremiumByRewardAd = null } };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.PremiumByAdReward, throwError: false);
        Assert.AreEqual(SessionErrorCode.AccessError, sessionDom.SessionResponseEx.ErrorCode);
    }


    [TestMethod]
    public async Task Trial_failed_if_not_set()
    {
        using var farm = await ServerFarmDom.Create();
        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", PremiumByTrial = null } };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.PremiumByTrial, throwError: false);
        Assert.AreEqual(SessionErrorCode.AccessError, sessionDom.SessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task Trial_success()
    {
        using var farm = await ServerFarmDom.Create();
        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", PremiumByTrial = 10 } };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.PremiumByTrial);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);
        Assert.IsTrue(sessionDom.SessionResponseEx.ExpirationTime > DateTime.UtcNow + TimeSpan.FromMinutes(8));
    }
    [TestMethod]
    public async Task Trial_extend_by_ad_reward()
    {
        using var farm = await ServerFarmDom.Create();
        farm.TestApp.AgentTestApp.AgentOptions.AdRewardPendingTimeout = TimeSpan.FromSeconds(1);
        farm.TestApp.AgentTestApp.AgentOptions.AdRewardRetryCount = 0; // we don't to retry for ad provider

        var clientPolicies = new[] { new ClientPolicy { ClientCountry = "*", PremiumByTrial = 10, PremiumByRewardAd = 20} };
        var accessTokenDom = await farm.CreateAccessToken(isPublic: true, clientPolicies: clientPolicies);
        var sessionDom = await accessTokenDom.CreateSession(planId: ConnectPlanId.PremiumByTrial);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);
        var totalMinutes = sessionDom.SessionResponseEx.ExpirationTime - DateTime.UtcNow;
        Assert.IsTrue(totalMinutes?.TotalMinutes is > 8 and < 12);

        // extend session with new code
        var adData = Guid.NewGuid().ToString();
        farm.TestApp.AgentTestApp.CacheService.Ad_AddRewardData(farm.ProjectId, adData);
        var sessionResponse = await sessionDom.AddUsage(adData: adData, traffic: new Traffic());
        totalMinutes = sessionResponse.ExpirationTime - DateTime.UtcNow;
        Assert.IsTrue(totalMinutes?.TotalMinutes is > 25 and < 35);
    }
}
