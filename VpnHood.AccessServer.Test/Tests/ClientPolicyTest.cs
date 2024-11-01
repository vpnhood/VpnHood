using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;

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
    public async Task Connect_accept_by_Free()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        var server10 = await farm.AddNewServer(publicIpV4: IPAddress.Parse("10.0.0.1"), logicalCore: 1);
        var server11 = await farm.AddNewServer(publicIpV4: IPAddress.Parse("11.0.0.2"), logicalCore: 1);
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
                    CountryCode = "2",
                    FreeLocations = ["12"],
                    PremiumByTrial = 100,
                    PremiumByRewardAd = 200,
                    Normal = 50,
                    PremiumByPurchase = true,
                    AutoLocationOnly = true,
                    PremiumByCode = true
                }
            ]
        };

        var accessTokenDom = await farm.CreateAccessToken(createParam);

        // all country except 80 can select server10
        var session = await accessTokenDom.CreateSession(serverLocation: "*", clientIp: IPAddress.Parse("1.0.0.01"), autoRedirect: true);
        Assert.AreEqual(server10.ServerId, session.ServerId);
        server10.ServerInfo.Status.SessionCount++;
        await server10.SendStatus();

        session = await accessTokenDom.CreateSession(serverLocation: "*", clientIp: IPAddress.Parse("2.0.0.01"), autoRedirect: true);
        Assert.AreEqual(server12.ServerId, session.ServerId);
        server12.ServerInfo.Status.SessionCount++;
        await server12.SendStatus();

    }
}