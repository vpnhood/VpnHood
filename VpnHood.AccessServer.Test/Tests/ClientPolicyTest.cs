using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Agent.Exceptions;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Tokens;
using ClientPolicy = VpnHood.AccessServer.Api.ClientPolicy;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ClientPolicyTest
{
    [TestMethod]
    public async Task Crud()
    {
        using var farm = await ServerFarmDom.Create();

        // create a public access token
        var createParam = new AccessTokenCreateParams {
            IsPublic = true,
            ClientPolicies = [
                new ClientPolicy {
                    CountryCode = "*",
                    FreeLocations = ["US"],
                    PremiumByTrial = 10,
                    PremiumByRewardAd = 20,
                    Normal = 5,
                    PremiumByPurchase = false,
                    AutoLocationOnly = false,
                    PremiumByCode = false
                },
                new ClientPolicy {
                    CountryCode = "CA",
                    FreeLocations = ["US", "CA"],
                    PremiumByTrial = 100,
                    PremiumByRewardAd = 200,
                    Normal = 50,
                    PremiumByPurchase = true,
                    AutoLocationOnly = true,
                    PremiumByCode = true
                },
                new ClientPolicy {
                    CountryCode = "CN",
                    FreeLocations = [],
                    PremiumByTrial = 50,
                    PremiumByRewardAd = 60,
                    Normal = 70,
                    PremiumByPurchase = true
                }
            ]
        };

        var accessTokenDom = await farm.CreateAccessToken(createParam);

        Assert.AreEqual(
            JsonSerializer.Serialize(createParam.ClientPolicies),
            JsonSerializer.Serialize(accessTokenDom.AccessToken.ClientPolicies));

        // update
        var updateParams = new AccessTokenUpdateParams {
            ClientPolicies = new PatchOfClientPolicyOf {
                Value = [
                    new ClientPolicy {
                        CountryCode = "*",
                        FreeLocations = ["BR"],
                        PremiumByTrial = 10,
                        PremiumByRewardAd = 20,
                        Normal = 5,
                        PremiumByPurchase = false,
                        AutoLocationOnly = false,
                        PremiumByCode = false
                    },
                    new ClientPolicy {
                        CountryCode = "FR",
                        FreeLocations = ["FR", "CA"],
                        PremiumByTrial = 10,
                        PremiumByRewardAd = 20,
                        Normal = 500,
                        PremiumByPurchase = true,
                        AutoLocationOnly = false,
                        PremiumByCode = false
                    }
                ]
            }
        };

        await accessTokenDom.Update(updateParams);
        await accessTokenDom.Reload();

        Assert.AreEqual(
            JsonSerializer.Serialize(updateParams.ClientPolicies.Value),
            JsonSerializer.Serialize(accessTokenDom.AccessToken.ClientPolicies));
    }

    [TestMethod]
    public async Task AccessKey_must_contain_policy()
    {
        using var farm = await ServerFarmDom.Create();

        // create a public access token
        var createParam = new AccessTokenCreateParams {
            IsPublic = true,
            ClientPolicies = [
                new ClientPolicy {
                    CountryCode = "*",
                    FreeLocations = ["US"],
                    PremiumByTrial = 10,
                    PremiumByRewardAd = 20,
                    Normal = 5,
                    PremiumByPurchase = false,
                    AutoLocationOnly = false,
                    PremiumByCode = false
                },
                new ClientPolicy {
                    CountryCode = "CA",
                    FreeLocations = ["US", "CA"],
                    PremiumByTrial = 100,
                    PremiumByRewardAd = 200,
                    Normal = 50,
                    PremiumByPurchase = true,
                    AutoLocationOnly = true,
                    PremiumByCode = true
                }
            ]
        };

        var policies = createParam.ClientPolicies.ToArray();
        var accessTokenDom = await farm.CreateAccessToken(createParam);
        var token = await accessTokenDom.GetToken();

        var actual = token.ClientPolicies?.First(x => x.CountryCode == "*");
        var expected = policies[0];
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.CountryCode, actual.CountryCode);
        CollectionAssert.AreEqual(expected.FreeLocations?.ToArray(), actual.FreeLocations);
        Assert.AreEqual(expected.PremiumByTrial, actual.PremiumByTrial);
        Assert.AreEqual(expected.PremiumByRewardAd, actual.PremiumByRewardAd);
        Assert.AreEqual(expected.Normal, actual.Normal);
        Assert.AreEqual(expected.PremiumByPurchase, actual.PremiumByPurchase);
        Assert.AreEqual(expected.AutoLocationOnly, actual.AutoLocationOnly);
        Assert.AreEqual(expected.PremiumByCode, actual.PremiumByCode);

        actual = token.ClientPolicies?.First(x => x.CountryCode == "CA");
        expected = policies[1];
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.CountryCode, actual.CountryCode);
        CollectionAssert.AreEqual(expected.FreeLocations?.ToArray(), actual.FreeLocations);
        Assert.AreEqual(expected.PremiumByTrial, actual.PremiumByTrial);
        Assert.AreEqual(expected.PremiumByRewardAd, actual.PremiumByRewardAd);
        Assert.AreEqual(expected.Normal, actual.Normal);
        Assert.AreEqual(expected.PremiumByPurchase, actual.PremiumByPurchase);
        Assert.AreEqual(expected.AutoLocationOnly, actual.AutoLocationOnly);
        Assert.AreEqual(expected.PremiumByCode, actual.PremiumByCode);
    }

    [TestMethod]
    public async Task Connect_accept_by_free_location_or_premium_account()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        var server10 = await farm.AddNewServer(publicIpV4: IPAddress.Parse("10.0.0.1"), logicalCore: 1);
        var server12 = await farm.AddNewServer(publicIpV4: IPAddress.Parse("12.0.0.2"), logicalCore: 1);

        // create a public access token
        var createParam = new AccessTokenCreateParams {
            IsPublic = true,
            ClientPolicies = [
                new ClientPolicy {
                    CountryCode = "*",
                    FreeLocations = ["10"],
                    PremiumByTrial = 10,
                    PremiumByRewardAd = 20,
                    Normal = 5,
                    PremiumByPurchase = false,
                    AutoLocationOnly = false,
                    PremiumByCode = false
                },
                new ClientPolicy {
                    CountryCode = "20",
                    FreeLocations = ["12"],
                    PremiumByTrial = 100,
                    PremiumByRewardAd = 200,
                    Normal = 50,
                    PremiumByPurchase = true,
                    AutoLocationOnly = false,
                    PremiumByCode = true
                }
            ]
        };

        var accessTokenDom = await farm.CreateAccessToken(createParam);

        // countries that are not 20 can select location 10
        var session = await accessTokenDom.CreateSession(serverLocation: "*", clientIp: IPAddress.Parse("1.0.0.01"), autoRedirect: true);
        Assert.AreEqual(server10.ServerId, session.ServerId);
        server10.ServerInfo.Status.SessionCount++;
        await server10.SendStatus();

        // Country 20 can select location 12
        session = await accessTokenDom.CreateSession(serverLocation: "*", clientIp: IPAddress.Parse("20.0.0.01"), autoRedirect: true);
        Assert.AreEqual(server12.ServerId, session.ServerId);
        server12.ServerInfo.Status.SessionCount++;
        await server12.SendStatus();

        // Country 20 can not select location 10
        await Assert.ThrowsExceptionAsync<SessionExceptionEx>(() =>
            accessTokenDom.CreateSession(serverLocation: "10", clientIp: IPAddress.Parse("20.0.0.01"), autoRedirect: true));

        // Country 20 can not select location 10 if it is a premium account
        createParam.Tags = [TokenRegisteredTags.Premium];
        accessTokenDom = await farm.CreateAccessToken(createParam);
        session = await accessTokenDom.CreateSession(serverLocation: "10", clientIp: IPAddress.Parse("20.0.0.01"), autoRedirect: true);
        Assert.AreEqual(server10.ServerId, session.ServerId);
    }

    [TestMethod]
    public async Task Premium_server_must_be_selected_for_premium_users()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        var freeServer = await farm.AddNewServer(publicIpV4: IPAddress.Parse("10.0.0.1"), logicalCore: 1);
        var prmServer = await farm.AddNewServer(publicIpV4: IPAddress.Parse("10.0.0.2"), logicalCore: 1, tags: [ServerRegisteredTags.Premium]);

        // only free server can be selected
        var createParam = new AccessTokenCreateParams {
            IsPublic = true,
            ClientPolicies = [
                new ClientPolicy {
                    CountryCode = "*",
                    FreeLocations = ["10"],
                    PremiumByTrial = 10,
                    PremiumByRewardAd = 20,
                    Normal = 5,
                    PremiumByPurchase = false,
                    AutoLocationOnly = false,
                    PremiumByCode = false
                }
            ]
        };

        var accessTokenDom = await farm.CreateAccessToken(createParam);

        SessionDom sessionDom;
        for (var i = 0; i < 4; i++) {
            sessionDom = await accessTokenDom.CreateSession(serverLocation: "*", clientIp: IPAddress.Parse("1.0.0.01"), autoRedirect: true);
            Assert.AreEqual(freeServer.ServerId, sessionDom.ServerId);
            freeServer.ServerInfo.Status.SessionCount++;
            await freeServer.SendStatus();
        }

        // premium server must be selected for premium users
        createParam = new AccessTokenCreateParams {
            IsPublic = false,
            Tags = [TokenRegisteredTags.Premium],
            ClientPolicies = [
                new ClientPolicy {
                    CountryCode = "*",
                    FreeLocations = ["10"],
                    PremiumByTrial = 10,
                    PremiumByRewardAd = 20,
                    Normal = 5,
                    PremiumByPurchase = false,
                    AutoLocationOnly = false,
                    PremiumByCode = false
                }
            ]
        };

        accessTokenDom = await farm.CreateAccessToken(createParam);
        for (var i = 0; i < 10; i++) {
            sessionDom = await accessTokenDom.CreateSession(serverLocation: "*", clientIp: IPAddress.Parse("1.0.0.01"), autoRedirect: true);
            Assert.AreEqual(prmServer.ServerId, sessionDom.ServerId);
            prmServer.ServerInfo.Status.SessionCount++;
            await prmServer.SendStatus();
        }
    }

    [TestMethod]
    public async Task Unblockable_server_must_be_listed_at_end()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        var freeServer0 = await farm.AddNewServer(publicIpV4: IPAddress.Parse("10.0.0.0"), logicalCore: 1);
        var freeServer1 = await farm.AddNewServer(publicIpV4: IPAddress.Parse("10.0.0.1"), logicalCore: 1);
        var ublServer = await farm.AddNewServer(publicIpV4: IPAddress.Parse("10.0.0.2"), logicalCore: 1, tags: [ServerRegisteredTags.Unblockable]);

        // only free server can be selected
        var createParam = new AccessTokenCreateParams {
            IsPublic = true,
            ClientPolicies = [
                new ClientPolicy {
                    CountryCode = "*",
                    FreeLocations = ["10"],
                    PremiumByTrial = 10,
                    PremiumByRewardAd = 20,
                    Normal = 5,
                    PremiumByPurchase = false,
                    AutoLocationOnly = false,
                    PremiumByCode = false
                }
            ]
        };

        var accessTokenDom = await farm.CreateAccessToken(createParam);

        // increase the session count of the free servers to force redirect to the unblockable server
        freeServer0.ServerInfo.Status.SessionCount = 100;
        await freeServer0.SendStatus();

        freeServer1.ServerInfo.Status.SessionCount = 80;
        await freeServer1.SendStatus();

        for (var i = 0; i < 10; i++) {
            var sessionDom = await accessTokenDom.CreateSession(serverLocation: "*", clientIp: IPAddress.Parse("1.0.0.01"), autoRedirect: false, throwError: false);
            Assert.AreEqual(freeServer1.Server.PublicIpV4, sessionDom.SessionResponseEx.RedirectHostEndPoints?[0].Address.ToString());
            Assert.AreEqual(freeServer0.Server.PublicIpV4, sessionDom.SessionResponseEx.RedirectHostEndPoints?[1].Address.ToString());
            Assert.AreEqual(ublServer.Server.PublicIpV4, sessionDom.SessionResponseEx.RedirectHostEndPoints?.Last().Address.ToString());
            freeServer0.ServerInfo.Status.SessionCount++;
            await freeServer0.SendStatus();
        }
    }
}