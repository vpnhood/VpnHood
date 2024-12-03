using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client.App.ClientProfiles;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Tokens;

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientProfileTest : TestBase
{
    private int _lastSupportId;
    private Token CreateToken()
    {
        var randomId = Guid.NewGuid();
        var token = new Token {
            Name = "Default Test Server",
            IssuedAt = DateTime.UtcNow,
            SupportId = _lastSupportId++.ToString(),
            TokenId = randomId.ToString(),
            Secret = randomId.ToByteArray(),
            ServerToken = new ServerToken {
                HostEndPoints = [IPEndPoint.Parse("127.0.0.1:443")],
                CertificateHash = randomId.ToByteArray(),
                HostName = randomId.ToString(),
                HostPort = 443,
                Secret = randomId.ToByteArray(),
                CreatedTime = DateTime.UtcNow,
                IsValidHostName = false
            }
        };

        return token;
    }

    [TestMethod]
    public async Task BuiltIn_AccessKeys_initialization()
    {
        var appOptions = TestHelper.CreateAppOptions();
        var tokens = new[] { CreateToken(), CreateToken() };
        appOptions.AccessKeys = tokens.Select(x => x.ToAccessKey()).ToArray();

        await using var app1 = TestHelper.CreateClientApp(appOptions: appOptions);
        var clientProfiles = app1.ClientProfileService.List();
        Assert.AreEqual(tokens.Length, clientProfiles.Length);
        Assert.AreEqual(tokens[0].TokenId, clientProfiles[0].Token.TokenId);
        Assert.AreEqual(tokens[1].TokenId, clientProfiles[1].Token.TokenId);
        Assert.AreEqual(tokens[0].TokenId,
            clientProfiles.Single(x => x.ClientProfileId == app1.Features.BuiltInClientProfileId).Token.TokenId);

        // BuiltIn token should not be removed
        foreach (var clientProfile in clientProfiles) {
            Assert.ThrowsException<UnauthorizedAccessException>(() => {
                // ReSharper disable once AccessToDisposedClosure
                app1.ClientProfileService.Remove(clientProfile.ClientProfileId);
            });
        }
    }

    [TestMethod]
    public async Task BuiltIn_AccessKeys_RemoveOldKeys()
    {
        var appOptions = TestHelper.CreateAppOptions();
        var tokens1 = new[] { CreateToken(), CreateToken() };
        appOptions.AccessKeys = tokens1.Select(x => x.ToAccessKey()).ToArray();

        await using var app1 = TestHelper.CreateClientApp(appOptions: appOptions);
        await app1.DisposeAsync();

        // create app again
        var tokens2 = new[] { CreateToken(), CreateToken() };
        appOptions.AccessKeys = tokens2.Select(x => x.ToAccessKey()).ToArray();
        await using var app2 = TestHelper.CreateClientApp(appOptions: appOptions);

        var clientProfiles = app2.ClientProfileService.List();
        Assert.AreEqual(tokens2.Length, clientProfiles.Length);
        Assert.AreEqual(tokens2[0].TokenId, clientProfiles[0].Token.TokenId);
        Assert.AreEqual(tokens2[1].TokenId, clientProfiles[1].Token.TokenId);
        foreach (var clientProfile in clientProfiles)
            Assert.IsTrue(clientProfile.ToInfo().IsBuiltIn);
    }

    [TestMethod]
    public async Task ClientPolicy()
    {
        await using var app = TestHelper.CreateClientApp();

        // test two region in a same country
        var token = CreateToken();
        token.IsPublic = true;
        var defaultPolicy = new ClientPolicy {
            ClientCountries = ["*"],
            FreeLocations = ["US", "CA"],
            Normal = 10,
            PremiumByPurchase = true,
            PremiumByRewardedAd = 20,
            PremiumByTrial = 30
        };
        var caPolicy = new ClientPolicy {
            ClientCountries = ["CA"],
            FreeLocations = ["CA"],
            PremiumByPurchase = true,
            Normal = 200,
            PremiumByTrial = 300
        };

        token.ClientPolicies = [defaultPolicy, caPolicy];

        token.ServerToken.ServerLocations = [
            "US", "US/California",
            "CA/Region1 [#premium]", "CA/Region2",
            "FR/Region1 [#premium]", "FR/Region2 [#premium]"
        ];

        // test free US client
        app.UpdateCurrentCountry("US");
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        var clientProfileInfo = clientProfile.ToInfo();

        // default (*/*)
        var location = clientProfileInfo.LocationInfos.Single(x => x.ServerLocation == "*/*");
        Assert.IsTrue(location.Options.HasFree);
        Assert.IsTrue(location.Options.HasPremium);
        Assert.IsTrue(location.Options.Prompt);
        Assert.AreEqual(defaultPolicy.Normal, location.Options.Normal);
        Assert.AreEqual(defaultPolicy.PremiumByRewardedAd, location.Options.PremiumByRewardedAd);
        Assert.AreEqual(defaultPolicy.PremiumByTrial, location.Options.PremiumByTrial);

        // (US/*) there is no premium server here
        location = clientProfileInfo.LocationInfos.Single(x => x.ServerLocation == "US/*");
        Assert.IsTrue(location.Options.HasFree);
        Assert.IsFalse(location.Options.HasPremium);
        Assert.IsFalse(location.Options.Prompt);
        Assert.AreEqual(defaultPolicy.Normal, location.Options.Normal);
        Assert.IsNull(location.Options.PremiumByRewardedAd);
        Assert.IsNull(location.Options.PremiumByTrial);

        // (FR/*) just premium
        location = clientProfileInfo.LocationInfos.Single(x => x.ServerLocation == "FR/*");
        Assert.IsFalse(location.Options.HasFree);
        Assert.IsTrue(location.Options.HasPremium);
        Assert.IsTrue(location.Options.Prompt);
        Assert.IsNull(location.Options.Normal);
        Assert.AreEqual(defaultPolicy.PremiumByRewardedAd, location.Options.PremiumByRewardedAd);
        Assert.AreEqual(defaultPolicy.PremiumByTrial, location.Options.PremiumByTrial);

        // (US/*) no free for CA clients
        app.UpdateCurrentCountry("CA");
        clientProfileInfo = app.ClientProfileService.Get(clientProfileInfo.ClientProfileId).ToInfo();
        location = clientProfileInfo.LocationInfos.Single(x => x.ServerLocation == "US/*");
        Assert.IsFalse(location.Options.HasFree);
        Assert.IsTrue(location.Options.HasPremium);
        Assert.IsTrue(location.Options.Prompt);
        Assert.IsNull(location.Options.Normal);
        Assert.AreEqual(caPolicy.PremiumByRewardedAd, location.Options.PremiumByRewardedAd);
        Assert.AreEqual(caPolicy.PremiumByTrial, location.Options.PremiumByTrial);

        // create premium token
        token.IsPublic = false;
        clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        clientProfileInfo = clientProfile.ToInfo();
        location = clientProfileInfo.LocationInfos.Single(x => x.ServerLocation == "FR/*");
        Assert.IsFalse(location.Options.HasFree);
        Assert.IsTrue(location.Options.HasPremium);
        Assert.IsFalse(location.Options.Prompt);
        Assert.AreEqual(0, location.Options.Normal);
        Assert.IsNull(location.Options.PremiumByRewardedAd);
        Assert.IsNull(location.Options.PremiumByTrial);
        Assert.IsFalse(location.Options.PremiumByPurchase);
    }


    [TestMethod]
    public async Task Crud()
    {
        await using var app = TestHelper.CreateClientApp();

        // ************
        // *** TEST ***: AddAccessKey should add a clientProfile
        var token1 = CreateToken();
        token1.ServerToken.ServerLocations = ["us", "us/california"];
        var clientProfile = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        Assert.IsNotNull(app.ClientProfileService.FindByTokenId(token1.TokenId), "ClientProfile is not added");
        Assert.AreEqual(token1.TokenId, clientProfile.Token.TokenId,
            "invalid tokenId has been assigned to clientProfile");

        // ************
        // *** TEST ***: AddAccessKey with new accessKey should add another clientProfile
        var token2 = CreateToken();
        app.ClientProfileService.ImportAccessKey(token2.ToAccessKey());
        Assert.IsNotNull(app.ClientProfileService.FindByTokenId(token1.TokenId), "ClientProfile is not added");

        // ************
        // *** TEST ***: AddAccessKey by same accessKey should just update token
        var profileCount = app.ClientProfileService.List().Length;
        token1.Name = "Token 1000";
        app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        Assert.AreEqual(token1.Name, app.ClientProfileService.GetToken(token1.TokenId).Name);
        Assert.AreEqual(profileCount, app.ClientProfileService.List().Length);

        // ************
        // *** TEST ***: Update throw NotExistsException exception if tokenId does not exist
        Assert.ThrowsException<NotExistsException>(() => {
            // ReSharper disable once AccessToDisposedClosure
            app.ClientProfileService.Update(Guid.NewGuid(), new ClientProfileUpdateParams {
                ClientProfileName = "Hi"
            });
        });

        // ************
        // *** TEST ***: Update should update the old node if ClientProfileId already exists
        var updateParams = new ClientProfileUpdateParams {
            ClientProfileName = Guid.NewGuid().ToString(),
            IsFavorite = true,
            CustomData = Guid.NewGuid().ToString(),
            IsPremiumLocationSelected = true,
            SelectedLocation = "us/california"
        };
        app.ClientProfileService.Update(clientProfile.ClientProfileId, updateParams);
        clientProfile = app.ClientProfileService.Get(clientProfile.ClientProfileId);
        Assert.AreEqual(updateParams.ClientProfileName.Value, clientProfile.ClientProfileName);
        Assert.AreEqual(updateParams.IsFavorite.Value, clientProfile.IsFavorite);
        Assert.AreEqual(updateParams.CustomData.Value, clientProfile.CustomData);
        Assert.AreEqual(updateParams.IsPremiumLocationSelected.Value, clientProfile.IsPremiumLocationSelected);
        Assert.AreEqual(updateParams.SelectedLocation.Value, clientProfile.SelectedLocation);

        // ************
        // *** TEST ***: RemoveClientProfile
        app.ClientProfileService.Remove(clientProfile.ClientProfileId);
        Assert.IsNull(app.ClientProfileService.FindById(clientProfile.ClientProfileId),
            "ClientProfile has not been removed!");
    }

    [TestMethod]
    public async Task Save_load()
    {
        await using var app1 = TestHelper.CreateClientApp();

        var token1 = CreateToken();
        var clientProfile1 = app1.ClientProfileService.ImportAccessKey(token1.ToAccessKey());

        var token2 = CreateToken();
        var clientProfile2 = app1.ClientProfileService.ImportAccessKey(token2.ToAccessKey());

        var clientProfiles = app1.ClientProfileService.List();
        await app1.DisposeAsync();

        var appOptions = TestHelper.CreateAppOptions();
        appOptions.StorageFolderPath = app1.StorageFolderPath;

        await using var app2 = TestHelper.CreateClientApp(appOptions: appOptions);
        Assert.AreEqual(clientProfiles.Length, app2.ClientProfileService.List().Length,
            "ClientProfiles count are not same!");
        Assert.IsNotNull(app2.ClientProfileService.FindById(clientProfile1.ClientProfileId));
        Assert.IsNotNull(app2.ClientProfileService.FindById(clientProfile2.ClientProfileId));
        Assert.IsNotNull(app2.ClientProfileService.GetToken(token1.TokenId));
        Assert.IsNotNull(app2.ClientProfileService.GetToken(token2.TokenId));
    }

    [TestMethod]
    public async Task Default_ServerLocation()
    {
        await using var app = TestHelper.CreateClientApp();

        // test two region in a same country
        var token = CreateToken();
        token.ServerToken.ServerLocations = null;

        // if there is no server location, it should be null
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey()).ToInfo();
        Assert.IsNull(clientProfile.SelectedLocationInfo?.ServerLocation);

        // if there is no server location, it should be null
        token.ServerToken.ServerLocations = [];
        clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey()).ToInfo();
        Assert.IsNull(clientProfile.SelectedLocationInfo?.ServerLocation);

        // if no server location is set, it should return the first server location
        token.ServerToken.ServerLocations = ["US/California"];
        clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey()).ToInfo();
        Assert.AreEqual("US/California", clientProfile.SelectedLocationInfo?.ServerLocation);

        // if null server location is set, it should return the first server location
        app.ClientProfileService.Update(clientProfile.ClientProfileId,
            new ClientProfileUpdateParams { SelectedLocation = null });
        Assert.AreEqual("US/California", clientProfile.SelectedLocationInfo?.ServerLocation);

        // if wrong server location is set for one location, it should return the first server location
        app.ClientProfileService.Update(clientProfile.ClientProfileId,
            new ClientProfileUpdateParams { SelectedLocation = "US/Cal_Wrong" });
        Assert.AreEqual("US/California", clientProfile.SelectedLocationInfo?.ServerLocation);

        // if no server location is set for two location, it should return auto
        token.ServerToken.ServerLocations = ["US/California", "FR/Paris"];
        clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey()).ToInfo();
        Assert.IsTrue(ServerLocationInfo.IsAutoLocation(clientProfile.SelectedLocationInfo?.ServerLocation));

        // if wrong server location is set for two location, it should return auto
        token.ServerToken.ServerLocations = ["US/California", "FR/Paris"];
        Assert.IsTrue(ServerLocationInfo.IsAutoLocation(clientProfile.SelectedLocationInfo?.ServerLocation));
        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams { SelectedLocation = "US/Cal_Wrong" });
    }


    [TestMethod]
    public async Task Calculate_server_location_tags()
    {
        await using var app = TestHelper.CreateClientApp();

        // test two region in a same country
        var token = CreateToken();
        token.ServerToken.ServerLocations = ["US/texas [#tag1]", "US/california [#tag1 #tag2]"];

        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.ClientProfileId = clientProfile.ClientProfileId;

        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams { SelectedLocation = "US/*" });
        Assert.AreEqual("US/*", app.State.ClientProfile?.SelectedLocationInfo?.ServerLocation);
        CollectionAssert.AreEquivalent(new[] { "#tag1", "~#tag2" }, app.State.ClientProfile?.SelectedLocationInfo?.Tags);

        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams { SelectedLocation = "US/california" });
        CollectionAssert.AreEquivalent(new[] { "#tag1", "#tag2" }, app.State.ClientProfile?.SelectedLocationInfo?.Tags);

        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams { SelectedLocation = "US/texas" });
        CollectionAssert.AreEquivalent(new[] { "#tag1", }, app.State.ClientProfile?.SelectedLocationInfo?.Tags);

        // test three regin
        token = CreateToken();
        token.ServerToken.ServerLocations = ["US/texas", "US/california [#z1 #z2]", "FR/paris [#p1 #p2]"];
        clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.ClientProfileId = clientProfile.ClientProfileId;
        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams { SelectedLocation = "FR/paris" });
        CollectionAssert.AreEquivalent(new[] { "#p1", "#p2" }, app.State.ClientProfile?.SelectedLocationInfo?.Tags);

        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams { SelectedLocation = "*/*" });
        app.Settings.Save();
        CollectionAssert.AreEquivalent(new[] { "~#p1", "~#p2", "~#z1", "~#z2" }, app.State.ClientProfile?.SelectedLocationInfo?.Tags);
    }

    [TestMethod]
    public async Task Create_parent_ServerLocations()
    {
        await using var app1 = TestHelper.CreateClientApp();

        // test two region in a same country
        var token = CreateToken();
        token.ServerToken.ServerLocations = ["US", "US/california"];
        var clientProfile = app1.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        var clientProfileInfo = clientProfile.ToInfo();
        var serverLocations = clientProfileInfo.LocationInfos.Select(x => x.ServerLocation).ToArray();
        var i = 0;
        Assert.AreEqual("US/*", serverLocations[i++]);
        Assert.AreEqual("US/california", serverLocations[i++]);
        Assert.IsFalse(clientProfileInfo.LocationInfos[0].IsNestedCountry);
        Assert.IsTrue(clientProfileInfo.LocationInfos[0].IsDefault);
        Assert.IsTrue(clientProfileInfo.LocationInfos[1].IsNestedCountry);
        Assert.IsFalse(clientProfileInfo.LocationInfos[1].IsDefault);
        _ = i;

        // test multiple countries
        token = CreateToken();
        token.ServerToken.ServerLocations = ["US", "US/california", "uk"];
        clientProfile = app1.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        clientProfileInfo = clientProfile.ToInfo();
        serverLocations = clientProfileInfo.LocationInfos.Select(x => x.ServerLocation).ToArray();
        i = 0;
        Assert.AreEqual("*/*", serverLocations[i++]);
        Assert.AreEqual("UK/*", serverLocations[i++]);
        Assert.AreEqual("US/*", serverLocations[i++]);
        Assert.AreEqual("US/california", serverLocations[i++]);
        Assert.IsFalse(clientProfileInfo.LocationInfos[0].IsNestedCountry);
        Assert.IsTrue(clientProfileInfo.LocationInfos[0].IsDefault);
        _ = i;

        // test multiple countries
        token = CreateToken();
        token.ServerToken.ServerLocations = ["us/virgina", "us/california", "uk/england [#pr]", "uk/region2"];
        clientProfile = app1.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        clientProfileInfo = clientProfile.ToInfo();
        serverLocations = clientProfileInfo.LocationInfos.Select(x => x.ServerLocation).ToArray();
        i = 0;
        Assert.AreEqual("*/*", serverLocations[i++]);
        Assert.AreEqual("UK/*", serverLocations[i++]);
        Assert.AreEqual("UK/england", serverLocations[i++]);
        Assert.AreEqual("UK/region2", serverLocations[i++]);
        Assert.AreEqual("US/*", serverLocations[i++]);
        Assert.AreEqual("US/california", serverLocations[i++]);
        Assert.AreEqual("US/virgina", serverLocations[i++]);
        Assert.IsFalse(clientProfileInfo.LocationInfos[0].IsNestedCountry);
        Assert.IsFalse(clientProfileInfo.LocationInfos[1].IsNestedCountry);
        Assert.IsTrue(clientProfileInfo.LocationInfos[2].IsNestedCountry);
        Assert.IsTrue(clientProfileInfo.LocationInfos[3].IsNestedCountry);
        _ = i;
    }

    [TestMethod]
    public async Task Filter_unblockable()
    {
        await using var app = TestHelper.CreateClientApp();

        var defaultPolicy = new ClientPolicy {
            ClientCountries = ["*"],
            FreeLocations = ["US", "CA"],
            Normal = 10,
            PremiumByPurchase = true,
            PremiumByRewardedAd = 20,
            PremiumByTrial = 30,
            UnblockableOnly = true,
        };

        // test two region in a same country
        var token = CreateToken();
        token.ClientPolicies = [defaultPolicy];
        token.ServerToken.ServerLocations = [
            "US/texas [#tag1]",
            "US/california [#tag1 #unblockable]",
            "CA/toronto [#tag1]",
            "UK/london [#unblockable]",
        ];

        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.ClientProfileId = clientProfile.ClientProfileId;

        var clientProfileInfo = clientProfile.ToInfo();
        // test three regin
        Assert.IsTrue(clientProfileInfo.LocationInfos.Any(x => x.ServerLocation == "US/*"));
        Assert.IsTrue(clientProfileInfo.LocationInfos.Any(x => x.ServerLocation == "US/california"));
        Assert.IsFalse(clientProfileInfo.LocationInfos.Any(x => x.ServerLocation == "UK/*"));
        Assert.IsTrue(clientProfileInfo.LocationInfos.Any(x => x.ServerLocation == "UK/london"));
        Assert.IsFalse(clientProfileInfo.LocationInfos.Any(x => x.ServerLocation == "US/texas"));
        Assert.IsFalse(clientProfileInfo.LocationInfos.Any(x => x.ServerLocation == "CA/*"));
        Assert.IsFalse(clientProfileInfo.LocationInfos.Any(x => x.ServerLocation == "CA/toronto"));
    }
}