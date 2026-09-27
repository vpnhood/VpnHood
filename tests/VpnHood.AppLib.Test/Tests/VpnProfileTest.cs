using System.Net;
using VpnHood.AppLib.App.VpnProfiles;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.App.Services.Ads;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Common.Tokens;
using ClientPolicy = VpnHood.Core.Common.Tokens.ClientPolicy;
using VpnHood.Net.Toolkit.Exceptions;
using VpnHood.Net.Toolkit.Utils;

// ReSharper disable DisposeOnUsingVariable
namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class VpnProfileTest : TestAppBase
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
        var appOptions = TestAppHelper.CreateAppOptions();
        var tokens = new[] { CreateToken(), CreateToken() };
        appOptions.AccessKeys = [.. tokens.Select(x => x.ToAccessKey())];

        await using var app1 = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var vpnProfiles = app1.VpnProfileService.List();
        Assert.HasCount(tokens.Length, vpnProfiles);
        Assert.AreEqual(tokens[0].TokenId, vpnProfiles[0].Token.TokenId);
        Assert.AreEqual(tokens[1].TokenId, vpnProfiles[1].Token.TokenId);
        Assert.AreEqual(tokens[0].TokenId,
            vpnProfiles.Single(x => x.VpnProfileId == app1.UserSettings.VpnProfileId).Token.TokenId);

        // BuiltIn token should not be removed
        foreach (var vpnProfile in vpnProfiles) {
            Assert.ThrowsExactly<InvalidOperationException>(() => {
                // ReSharper disable once AccessToDisposedClosure
                app1.VpnProfileService.Delete(vpnProfile.VpnProfileId);
            });
        }
    }

    [TestMethod]
    public async Task BuiltIn_AccessKeys_RemoveOldKeys()
    {
        var appOptions = TestAppHelper.CreateAppOptions();
        var tokens1 = new[] { CreateToken(), CreateToken() };
        appOptions.AccessKeys = [.. tokens1.Select(x => x.ToAccessKey())];

        await using var app1 = TestAppHelper.CreateClientApp(appOptions: appOptions);
        await app1.DisposeAsync();

        // create app again
        var tokens2 = new[] { CreateToken(), CreateToken() };
        appOptions.AccessKeys = [.. tokens2.Select(x => x.ToAccessKey())];
        await using var app2 = TestAppHelper.CreateClientApp(appOptions: appOptions);

        var vpnProfiles = app2.VpnProfileService.List();
        Assert.HasCount(tokens2.Length, vpnProfiles);
        Assert.AreEqual(tokens2[0].TokenId, vpnProfiles[0].Token.TokenId);
        Assert.AreEqual(tokens2[1].TokenId, vpnProfiles[1].Token.TokenId);
        foreach (var vpnProfile in vpnProfiles)
            Assert.IsTrue(vpnProfile.ToInfo(app2.Features).IsBuiltIn);
    }

    [TestMethod]
    [DoNotParallelize] // mutates the process-global AppRegionInfo
    public async Task ClientPolicy()
    {
        using var accessManager = TestHelper.CreateAccessManager();

        var appOptions = TestAppHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestAppHelper.CreateClientApp(appOptions);

        // test two region in a same country
        var token = CreateToken();
        token.IsPublic = true;
        var defaultPolicy = new ClientPolicy {
            ClientCountries = ["*"],
            FreeLocations = ["US", "CA"],
            Normal = 10,
            NormalByRewardedAd = 15,
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
        app.UpdateClientCountry("US");
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        var vpnProfileInfo = vpnProfile.ToInfo(app.Features);

        // default (*/*)
        var location = vpnProfileInfo.LocationInfos.Single(x => x.ServerLocation == "*/*");
        Assert.IsTrue(location.Options.HasFree);
        Assert.IsTrue(location.Options.HasPremium);
        Assert.IsTrue(location.Options.Prompt);
        Assert.AreEqual(defaultPolicy.Normal, location.Options.Normal);
        Assert.AreEqual(defaultPolicy.NormalByRewardedAd, location.Options.NormalByRewardedAd);
        Assert.AreEqual(defaultPolicy.PremiumByRewardedAd, location.Options.PremiumByRewardedAd);
        Assert.AreEqual(defaultPolicy.PremiumByTrial, location.Options.PremiumByTrial);

        // (US/*) there is no premium server here, but free-by-rewarded-ad is still offered so it prompts
        location = vpnProfileInfo.LocationInfos.Single(x => x.ServerLocation == "US/*");
        Assert.IsTrue(location.Options.HasFree);
        Assert.IsFalse(location.Options.HasPremium);
        Assert.IsTrue(location.Options.Prompt);
        Assert.AreEqual(defaultPolicy.Normal, location.Options.Normal);
        Assert.AreEqual(defaultPolicy.NormalByRewardedAd, location.Options.NormalByRewardedAd);
        Assert.IsNull(location.Options.PremiumByRewardedAd);
        Assert.IsNull(location.Options.PremiumByTrial);

        // (FR/*) just premium
        location = vpnProfileInfo.LocationInfos.Single(x => x.ServerLocation == "FR/*");
        Assert.IsFalse(location.Options.HasFree);
        Assert.IsTrue(location.Options.HasPremium);
        Assert.IsTrue(location.Options.Prompt);
        Assert.IsNull(location.Options.Normal);
        Assert.IsNull(location.Options.NormalByRewardedAd);
        Assert.AreEqual(defaultPolicy.PremiumByRewardedAd, location.Options.PremiumByRewardedAd);
        Assert.AreEqual(defaultPolicy.PremiumByTrial, location.Options.PremiumByTrial);

        // (US/*) no free for CA clients
        app.UpdateClientCountry("CA");
        vpnProfileInfo = app.VpnProfileService.Get(vpnProfileInfo.VpnProfileId).ToInfo(app.Features);
        location = vpnProfileInfo.LocationInfos.Single(x => x.ServerLocation == "US/*");
        Assert.IsFalse(location.Options.HasFree);
        Assert.IsTrue(location.Options.HasPremium);
        Assert.IsTrue(location.Options.Prompt);
        Assert.IsNull(location.Options.Normal);
        Assert.IsNull(location.Options.NormalByRewardedAd);
        Assert.AreEqual(caPolicy.PremiumByRewardedAd, location.Options.PremiumByRewardedAd);
        Assert.AreEqual(caPolicy.PremiumByTrial, location.Options.PremiumByTrial);

        // create premium token
        token.IsPublic = false;
        vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        vpnProfileInfo = vpnProfile.ToInfo(app.Features);
        location = vpnProfileInfo.LocationInfos.Single(x => x.ServerLocation == "FR/*");
        Assert.IsFalse(location.Options.HasFree);
        Assert.IsTrue(location.Options.HasPremium);
        Assert.IsFalse(location.Options.Prompt);
        Assert.AreEqual(0, location.Options.Normal);
        Assert.IsNull(location.Options.NormalByRewardedAd);
        Assert.IsNull(location.Options.PremiumByRewardedAd);
        Assert.IsNull(location.Options.PremiumByTrial);
        Assert.IsFalse(location.Options.PremiumByPurchase);
    }

    [TestMethod]
    public async Task Crud_restore_to_auto_location_after_removing_access_code()
    {
        await using var app = TestAppHelper.CreateClientApp();
        // create access code
        var token = CreateToken();
        token.ServerToken.ServerLocations = ["us/california"];
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.VpnProfileId = vpnProfile.VpnProfileId;
        // update access code
        var accessCode = TestAppHelper.BuildAccessCode();
        app.VpnProfileService.Update(vpnProfile.VpnProfileId, new VpnProfileUpdateParams {
            AccessCode = accessCode
        });


        // remove access code
        vpnProfile = app.VpnProfileService.Update(vpnProfile.VpnProfileId, new VpnProfileUpdateParams {
            AccessCode = null
        });
        Assert.IsFalse(vpnProfile.IsPremiumLocationSelected);
        Assert.IsTrue(ServerLocationInfo.IsAutoLocation(vpnProfile.SelectedLocation));
    }


    [TestMethod]
    public async Task Crud()
    {
        await using var app = TestAppHelper.CreateClientApp();

        // ************
        // *** TEST ***: AddAccessKey should add a vpnProfile
        var token1 = CreateToken();
        token1.ServerToken.ServerLocations = ["us", "us/california"];
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token1.ToAccessKey());
        Assert.IsNotNull(app.VpnProfileService.FindByTokenId(token1.TokenId), "VpnProfile is not added");
        Assert.AreEqual(token1.TokenId, vpnProfile.Token.TokenId,
            "invalid tokenId has been assigned to vpnProfile");

        // ************
        // *** TEST ***: AddAccessKey with new accessKey should add another vpnProfile
        var token2 = CreateToken();
        app.VpnProfileService.ImportAccessKey(token2.ToAccessKey());
        Assert.IsNotNull(app.VpnProfileService.FindByTokenId(token1.TokenId), "VpnProfile is not added");

        // ************
        // *** TEST ***: AddAccessKey by same accessKey should just update token
        var profileCount = app.VpnProfileService.List().Length;
        token1.Name = "Token 1000";
        app.VpnProfileService.ImportAccessKey(token1.ToAccessKey());
        Assert.AreEqual(token1.Name, app.VpnProfileService.GetToken(token1.TokenId).Name);
        Assert.HasCount(profileCount, app.VpnProfileService.List());

        // ************
        // *** TEST ***: Update throw NotExistsException exception if tokenId does not exist
        Assert.ThrowsExactly<NotExistsException>(() => {
            // ReSharper disable once AccessToDisposedClosure
            app.VpnProfileService.Update(Guid.NewGuid(), new VpnProfileUpdateParams {
                VpnProfileName = "Hi"
            });
        });

        // ************
        // *** TEST ***: Update should update the old item if VpnProfileId already exists
        var updateParams = new VpnProfileUpdateParams {
            VpnProfileName = Guid.NewGuid().ToString(),
            IsFavorite = true,
            CustomData = Guid.NewGuid().ToString(),
            IsPremiumLocationSelected = true,
            SelectedLocation = "us/california",
            AccessCode = TestAppHelper.BuildAccessCode(),
            CustomServerEndpoints = new Patch<string[]?>(["1.1.1.1:200", "1.1.1.2:200"]),
            IsCustomServerEndpointsEnabled = false
        };
        app.VpnProfileService.Update(vpnProfile.VpnProfileId, updateParams);
        vpnProfile = app.VpnProfileService.Get(vpnProfile.VpnProfileId);
        Assert.AreEqual(updateParams.VpnProfileName.Value, vpnProfile.VpnProfileName);
        Assert.AreEqual(updateParams.IsFavorite.Value, vpnProfile.IsFavorite);
        CollectionAssert.AreEqual(updateParams.CustomServerEndpoints?.Value,
            vpnProfile.CustomServerEndpoints?.Select(x => x.ToString()).ToArray());
        Assert.AreEqual(updateParams.IsCustomServerEndpointsEnabled.Value, vpnProfile.IsCustomServerEndpointsEnabled);
        Assert.AreEqual(updateParams.CustomData.Value, vpnProfile.CustomData);
        Assert.AreEqual(updateParams.IsPremiumLocationSelected.Value, vpnProfile.IsPremiumLocationSelected);
        Assert.AreEqual(updateParams.SelectedLocation.Value, vpnProfile.SelectedLocation);
        Assert.AreEqual(updateParams.AccessCode.Value, vpnProfile.AccessCode);
        Assert.IsFalse(vpnProfile.IsAccessCodeSynced,
            "a code that appears here owes the account an upload — the service marks it, callers do not");
        Assert.AreEqual(AccessCodeUtils.Redact(updateParams.AccessCode.Value), vpnProfile.ToInfo(app.Features).AccessCode);

        // ************
        // *** TEST ***: RemoveVpnProfile
        app.VpnProfileService.Delete(vpnProfile.VpnProfileId);
        Assert.IsNull(app.VpnProfileService.FindById(vpnProfile.VpnProfileId),
            "VpnProfile has not been removed!");
    }

    [TestMethod]
    public async Task Save_load()
    {
        await using var app1 = TestAppHelper.CreateClientApp();

        var token1 = CreateToken();
        var vpnProfile1 = app1.VpnProfileService.ImportAccessKey(token1.ToAccessKey());

        var token2 = CreateToken();
        var vpnProfile2 = app1.VpnProfileService.ImportAccessKey(token2.ToAccessKey());

        var vpnProfiles = app1.VpnProfileService.List();
        await app1.DisposeAsync();

        var appOptions = TestAppHelper.CreateAppOptions(storagePath: app1.StorageFolderPath);
        await using var app2 = TestAppHelper.CreateClientApp(appOptions: appOptions);
        Assert.HasCount(vpnProfiles.Length, app2.VpnProfileService.List(), "VpnProfiles count are not same!");
        Assert.IsNotNull(app2.VpnProfileService.FindById(vpnProfile1.VpnProfileId));
        Assert.IsNotNull(app2.VpnProfileService.FindById(vpnProfile2.VpnProfileId));
        Assert.IsNotNull(app2.VpnProfileService.GetToken(token1.TokenId));
        Assert.IsNotNull(app2.VpnProfileService.GetToken(token2.TokenId));
    }

    [TestMethod]
    public async Task Default_ServerLocation()
    {
        await using var app = TestAppHelper.CreateClientApp();

        // test two region in a same country
        var token = CreateToken();
        token.ServerToken.ServerLocations = null;

        // if there is no server location, it should be null
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey()).ToInfo(app.Features);
        Assert.IsNull(vpnProfile.SelectedLocationInfo?.ServerLocation);

        // if there is no server location, it should be null
        token.ServerToken.ServerLocations = [];
        vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey()).ToInfo(app.Features);
        Assert.IsNull(vpnProfile.SelectedLocationInfo?.ServerLocation);

        // if no server location is set, it should return the first server location
        token.ServerToken.ServerLocations = ["US/California"];
        vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey()).ToInfo(app.Features);
        Assert.AreEqual("US/California", vpnProfile.SelectedLocationInfo?.ServerLocation);

        // if null server location is set, it should return the first server location
        app.VpnProfileService.Update(vpnProfile.VpnProfileId,
            new VpnProfileUpdateParams { SelectedLocation = null });
        Assert.AreEqual("US/California", vpnProfile.SelectedLocationInfo?.ServerLocation);

        // if wrong server location is set for one location, it should return the first server location
        app.VpnProfileService.Update(vpnProfile.VpnProfileId,
            new VpnProfileUpdateParams { SelectedLocation = "US/Cal_Wrong" });
        Assert.AreEqual("US/California", vpnProfile.SelectedLocationInfo?.ServerLocation);

        // if no server location is set for two location, it should return auto
        token.ServerToken.ServerLocations = ["US/California", "FR/Paris"];
        vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey()).ToInfo(app.Features);
        Assert.IsTrue(ServerLocationInfo.IsAutoLocation(vpnProfile.SelectedLocationInfo?.ServerLocation));

        // if wrong server location is set for two location, it should return auto
        token.ServerToken.ServerLocations = ["US/California", "FR/Paris"];
        Assert.IsTrue(ServerLocationInfo.IsAutoLocation(vpnProfile.SelectedLocationInfo?.ServerLocation));
        app.VpnProfileService.Update(vpnProfile.VpnProfileId,
            new VpnProfileUpdateParams { SelectedLocation = "US/Cal_Wrong" });
    }


    [TestMethod]
    public async Task Calculate_server_location_tags()
    {
        await using var app = TestAppHelper.CreateClientApp();

        // test two region in a same country
        var token = CreateToken();
        token.ServerToken.ServerLocations = ["US/texas [#tag1]", "US/california [#tag1 #tag2]"];

        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.VpnProfileId = vpnProfile.VpnProfileId;

        app.VpnProfileService.Update(vpnProfile.VpnProfileId,
            new VpnProfileUpdateParams { SelectedLocation = "US/*" });
        Assert.AreEqual("US/*", app.State.VpnProfile?.SelectedLocationInfo?.ServerLocation);
        CollectionAssert.AreEquivalent(new[] { "#tag1", "~#tag2" },
            app.State.VpnProfile?.SelectedLocationInfo?.Tags.ToArray());

        app.VpnProfileService.Update(vpnProfile.VpnProfileId,
            new VpnProfileUpdateParams { SelectedLocation = "US/california" });
        CollectionAssert.AreEquivalent(new[] { "#tag1", "#tag2" }, app.State.VpnProfile?.SelectedLocationInfo?.Tags.ToArray());

        app.VpnProfileService.Update(vpnProfile.VpnProfileId,
            new VpnProfileUpdateParams { SelectedLocation = "US/texas" });
        CollectionAssert.AreEquivalent(new[] { "#tag1" }, app.State.VpnProfile?.SelectedLocationInfo?.Tags.ToArray());

        // test three regin
        token = CreateToken();
        token.ServerToken.ServerLocations = ["US/texas", "US/california [#z1 #z2]", "FR/paris [#p1 #p2]"];
        vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.VpnProfileId = vpnProfile.VpnProfileId;
        app.VpnProfileService.Update(vpnProfile.VpnProfileId,
            new VpnProfileUpdateParams { SelectedLocation = "FR/paris" });
        CollectionAssert.AreEquivalent(new[] { "#p1", "#p2" }, app.State.VpnProfile?.SelectedLocationInfo?.Tags.ToArray());

        app.VpnProfileService.Update(vpnProfile.VpnProfileId,
            new VpnProfileUpdateParams { SelectedLocation = "*/*" });
        app.Settings.Save();
        CollectionAssert.AreEquivalent(new[] { "~#p1", "~#p2", "~#z1", "~#z2" },
            app.State.VpnProfile?.SelectedLocationInfo?.Tags.ToArray());
    }

    [TestMethod]
    public async Task Calculate_no_free_servers_tags()
    {
        await using var app = TestAppHelper.CreateClientApp();

        // test two region in a same country
        var token = CreateToken();

        var defaultPolicy = new ClientPolicy {
            ClientCountries = ["*"],
            FreeLocations = [],
            Normal = 0,
            PremiumByPurchase = true,
            PremiumByRewardedAd = 20,
            PremiumByTrial = 30,
            UnblockableOnly = false
        };
        token.ServerToken.ServerLocations =
            ["US/texas [#premium]", "US/california [#tag1 #tag2]", "US/arizona [~#premium]"];
        token.ClientPolicies = [defaultPolicy];
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        // get all locations
        var arizona = vpnProfile.ToInfo(app.Features).LocationInfos.First(x => x.ServerLocation == "US/arizona");
        Assert.IsFalse(arizona.Options.HasFree, "Free location should be overridden by FreeLocations.");
    }


    [TestMethod]
    public async Task Calculate_server_parent_location_tags_auto()
    {
        await using var app = TestAppHelper.CreateClientApp();
        var token = CreateToken();
        token.ServerToken.ServerLocations = ["US/texas [#tag1]", "US/california [#tag1]", "CA/toronto [#tag1]"];
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        var serverLocations = vpnProfile.ToInfo(app.Features).LocationInfos.ToArray();
        var autoLocation = serverLocations.Single(x => x.IsAuto);
        Assert.IsTrue(autoLocation.Tags?.Contains("#tag1"));
        Assert.IsFalse(autoLocation.Tags?.Contains("~#tag1"));
    }

    [TestMethod]
    public async Task Create_parent_ServerLocations()
    {
        await using var app1 = TestAppHelper.CreateClientApp();

        // test two region in a same country
        var token = CreateToken();
        token.ServerToken.ServerLocations = ["US", "US/california"];
        var vpnProfile = app1.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        var vpnProfileInfo = vpnProfile.ToInfo(app1.Features);
        var serverLocations = vpnProfileInfo.LocationInfos.Select(x => x.ServerLocation).ToArray();
        var i = 0;
        Assert.AreEqual("US/*", serverLocations[i++]);
        Assert.AreEqual("US/california", serverLocations[i++]);
        Assert.IsFalse(vpnProfileInfo.LocationInfos[0].IsNestedCountry);
        Assert.IsTrue(vpnProfileInfo.LocationInfos[0].IsDefault);
        Assert.IsTrue(vpnProfileInfo.LocationInfos[1].IsNestedCountry);
        Assert.IsFalse(vpnProfileInfo.LocationInfos[1].IsDefault);
        _ = i;

        // test multiple countries
        token = CreateToken();
        token.ServerToken.ServerLocations = ["US", "US/california", "uk"];
        vpnProfile = app1.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        vpnProfileInfo = vpnProfile.ToInfo(app1.Features);
        serverLocations = [.. vpnProfileInfo.LocationInfos.Select(x => x.ServerLocation)];
        i = 0;
        Assert.AreEqual("*/*", serverLocations[i++]);
        Assert.AreEqual("UK/*", serverLocations[i++]);
        Assert.AreEqual("US/*", serverLocations[i++]);
        Assert.AreEqual("US/california", serverLocations[i++]);
        Assert.IsFalse(vpnProfileInfo.LocationInfos[0].IsNestedCountry);
        Assert.IsTrue(vpnProfileInfo.LocationInfos[0].IsDefault);
        _ = i;

        // test multiple countries
        token = CreateToken();
        token.ServerToken.ServerLocations = ["us/virgina", "us/california", "uk/england [#pr]", "uk/region2"];
        vpnProfile = app1.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        vpnProfileInfo = vpnProfile.ToInfo(app1.Features);
        serverLocations = [.. vpnProfileInfo.LocationInfos.Select(x => x.ServerLocation)];
        i = 0;
        Assert.AreEqual("*/*", serverLocations[i++]);
        Assert.AreEqual("UK/*", serverLocations[i++]);
        Assert.AreEqual("UK/england", serverLocations[i++]);
        Assert.AreEqual("UK/region2", serverLocations[i++]);
        Assert.AreEqual("US/*", serverLocations[i++]);
        Assert.AreEqual("US/california", serverLocations[i++]);
        Assert.AreEqual("US/virgina", serverLocations[i++]);
        Assert.IsFalse(vpnProfileInfo.LocationInfos[0].IsNestedCountry);
        Assert.IsFalse(vpnProfileInfo.LocationInfos[1].IsNestedCountry);
        Assert.IsTrue(vpnProfileInfo.LocationInfos[2].IsNestedCountry);
        Assert.IsTrue(vpnProfileInfo.LocationInfos[3].IsNestedCountry);
        _ = i;
    }

    [TestMethod]
    public async Task Filter_unblockable()
    {
        await using var app = TestAppHelper.CreateClientApp();

        var defaultPolicy = new ClientPolicy {
            ClientCountries = ["*"],
            FreeLocations = ["US", "CA"],
            Normal = 10,
            PremiumByPurchase = true,
            PremiumByRewardedAd = 20,
            PremiumByTrial = 30,
            UnblockableOnly = true
        };

        // test two region in a same country
        var token = CreateToken();
        token.ClientPolicies = [defaultPolicy];
        token.ServerToken.ServerLocations = [
            "US/texas [#tag1]",
            "US/california [#tag1 #unblockable]",
            "CA/toronto [#tag1]",
            "UK/london [#unblockable]"
        ];

        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.VpnProfileId = vpnProfile.VpnProfileId;

        var vpnProfileInfo = vpnProfile.ToInfo(app.Features);
        // test three regin
        Assert.IsTrue(vpnProfileInfo.LocationInfos.Any(x => x.ServerLocation == "US/*"));
        Assert.IsTrue(vpnProfileInfo.LocationInfos.Any(x => x.ServerLocation == "US/california"));
        Assert.IsFalse(vpnProfileInfo.LocationInfos.Any(x => x.ServerLocation == "UK/*"));
        Assert.IsTrue(vpnProfileInfo.LocationInfos.Any(x => x.ServerLocation == "UK/london"));
        Assert.IsFalse(vpnProfileInfo.LocationInfos.Any(x => x.ServerLocation == "US/texas"));
        Assert.IsFalse(vpnProfileInfo.LocationInfos.Any(x => x.ServerLocation == "CA/*"));
        Assert.IsFalse(vpnProfileInfo.LocationInfos.Any(x => x.ServerLocation == "CA/toronto"));
    }

    [TestMethod]
    [DoNotParallelize] // mutates the process-global AppRegionInfo
    public async Task ClientPolicy_PurchaseUrl()
    {
        using var accessManager = TestHelper.CreateAccessManager();

        var appOptions = TestAppHelper.CreateAppOptions();
        var accountProvider = new TestAccountProvider();
        appOptions.AccountProvider = accountProvider;
        var billing = accountProvider.Billing ?? throw new InvalidOperationException("TestAccountProvider has no billing.");
        var billingProvider = (TestBillingProvider)billing.Provider;

        await using var app = TestAppHelper.CreateClientApp(appOptions);

        // The shop a policy names REPLACES the in-app store, and the policy that applies is chosen
        // by country — so the same build sells through the store in one country and through the
        // operator's own page in another, with the two never shown side by side.
        var token = CreateToken();
        token.IsPublic = true;
        var defaultPolicy = new ClientPolicy {
            ClientCountries = ["*"],
            Normal = 10
        };
        var caPolicy = new ClientPolicy {
            ClientCountries = ["CA"],
            FreeLocations = ["CA"],
            PremiumByPurchase = true,
            Normal = 200,
            PremiumByTrial = 300,
            PurchaseUrl = new Uri("http://localhost/ca")
        };
        var cnPolicy = new ClientPolicy {
            ClientCountries = ["CN"],
            FreeLocations = ["CN"],
            PremiumByPurchase = true,
            Normal = 200,
            PremiumByTrial = 300,
            PurchaseUrl = new Uri("http://localhost/cn")
        };

        token.ClientPolicies = [defaultPolicy, caPolicy, cnPolicy];

        // test default policy
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.VpnProfileId = vpnProfile.VpnProfileId;
        var vpnProfileInfo = vpnProfile.ToInfo(app.Features);

        // names no shop: the store is the only way in
        billingProvider.SubscriptionPlanException = null;
        var purchaseOptions = await app.GetPurchaseOptions(vpnProfileInfo.VpnProfileId, TestCt);
        Assert.IsNull(vpnProfileInfo.ClientPolicy?.PurchaseUrl);
        Assert.IsNull(purchaseOptions.PurchaseUrl);
        Assert.IsTrue(purchaseOptions.IsStoreAvailable);
        Assert.IsNull(purchaseOptions.StoreError);

        // names no shop and the store is broken: the failure is reported as itself, because there
        // is no outside shop to fall back to
        billingProvider.SubscriptionPlanException = new Exception("Billing Error");
        purchaseOptions = await app.GetPurchaseOptions(vpnProfileInfo.VpnProfileId, TestCt);
        Assert.IsNull(purchaseOptions.PurchaseUrl);
        Assert.IsNotNull(purchaseOptions.StoreError);

        // test ca policy: a named shop replaces the store, which is not even asked — the billing
        // provider is still broken here, and no store error may surface from a store nobody called
        app.UpdateClientCountry("CA");
        vpnProfileInfo = app.VpnProfileService.Get(vpnProfileInfo.VpnProfileId).ToInfo(app.Features);
        purchaseOptions = await app.GetPurchaseOptions(vpnProfileInfo.VpnProfileId, TestCt);
        Assert.AreEqual(caPolicy.PurchaseUrl, vpnProfileInfo.ClientPolicy?.PurchaseUrl);
        Assert.AreEqual(caPolicy.PurchaseUrl, purchaseOptions.PurchaseUrl);
        Assert.IsFalse(purchaseOptions.IsStoreAvailable, "a named shop must not be offered beside the store");
        Assert.IsNull(purchaseOptions.StoreError);
        billingProvider.SubscriptionPlanException = null;

        // test cn policy: the country picks the shop
        app.UpdateClientCountry("CN");
        vpnProfileInfo = app.VpnProfileService.Get(vpnProfileInfo.VpnProfileId).ToInfo(app.Features);
        purchaseOptions = await app.GetPurchaseOptions(vpnProfileInfo.VpnProfileId, TestCt);
        Assert.AreEqual(cnPolicy.PurchaseUrl, vpnProfileInfo.ClientPolicy?.PurchaseUrl);
        Assert.AreEqual(cnPolicy.PurchaseUrl, purchaseOptions.PurchaseUrl);
        Assert.IsFalse(purchaseOptions.IsStoreAvailable);
        Assert.IsNull(purchaseOptions.StoreError);
    }

    [TestMethod]
    [DoNotParallelize] // mutates the process-global AppRegionInfo
    public async Task ClientPolicy_PurchaseUrl_is_never_shown_by_a_store_build()
    {
        using var accessManager = TestHelper.CreateAccessManager();

        // a build shipped through a store: it may not steer a buyer to an outside shop, whatever the
        // token says — and the token is the one place the URL comes from, so this is the whole guard
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.Premium = new AppPremiumOptions { AllowImportAccessCode = true }; // and no outside shop
        appOptions.AccountProvider = new TestAccountProvider();
        await using var app = TestAppHelper.CreateClientApp(appOptions);

        var token = CreateToken();
        token.IsPublic = true;
        token.ServerToken.ServerLocations = ["US/California"];
        // the demanding case: a named shop replaces the store everywhere it is allowed, so obeying
        // it here would hide the store on behalf of a link this build will not show, leaving the
        // purchase page with nothing on it at all
        token.ClientPolicies = [
            new ClientPolicy {
                ClientCountries = ["*"],
                Normal = 10,
                PremiumByPurchase = true,
                PurchaseUrl = new Uri("http://localhost/shop")
            }
        ];

        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.VpnProfileId = vpnProfile.VpnProfileId;
        var vpnProfileInfo = vpnProfile.ToInfo(app.Features);

        var purchaseOptions = await app.GetPurchaseOptions(vpnProfile.VpnProfileId, TestCt);
        Assert.IsNull(purchaseOptions.PurchaseUrl, "a store build must never surface an outside shop");
        Assert.IsTrue(purchaseOptions.IsStoreAvailable, "the store must still be offered");

        // and the route in: a location must not advertise a purchase this build cannot complete
        Assert.IsTrue(vpnProfileInfo.SelectedLocationInfo?.Options.PremiumByPurchase,
            "the in-app store can complete it");

        appOptions = TestAppHelper.CreateAppOptions();
        appOptions.Premium = new AppPremiumOptions { AllowImportAccessCode = true }; // and no outside shop
        await using var appWithoutBilling = TestAppHelper.CreateClientApp(appOptions);
        var profileWithoutBilling = appWithoutBilling.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        var infoWithoutBilling = profileWithoutBilling.ToInfo(appWithoutBilling.Features);
        Assert.IsFalse(infoWithoutBilling.SelectedLocationInfo?.Options.PremiumByPurchase,
            "no store and no permitted shop leaves nothing to offer");
    }

    [TestMethod]
    public async Task A_build_that_takes_no_typed_code_still_shows_the_code_it_holds()
    {
        using var accessManager = TestHelper.CreateAccessManager();

        // the App Store head: App Review 3.1.1 reads a typed premium code as a license key, so this
        // build ships no box to type one — but the buyer's own subscription still produced a code,
        // and it is what they carry to their Android or Windows device, where typing it IS allowed
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.Premium = new AppPremiumOptions { AllowImportAccessCode = false };
        await using var app = TestAppHelper.CreateClientApp(appOptions);

        var token = CreateToken();
        token.IsPublic = true;
        token.ClientPolicies = [new ClientPolicy { ClientCountries = ["*"], Normal = 10, PremiumByCode = true }];

        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        var vpnProfileInfo = vpnProfile.ToInfo(app.Features);
        Assert.IsFalse(vpnProfileInfo.CanImportAccessCode, "this build offers no box to type a code in");
        Assert.IsTrue(vpnProfileInfo.CanViewAccessCode, "the operator allows codes, so the held one may be read");

        // the same token on a head that does take codes: both doors open
        var codeOptions = TestAppHelper.CreateAppOptions();
        codeOptions.Premium = new AppPremiumOptions { AllowImportAccessCode = true };
        await using var codeApp = TestAppHelper.CreateClientApp(codeOptions);
        var codeProfileInfo = codeApp.VpnProfileService.ImportAccessKey(token.ToAccessKey()).ToInfo(codeApp.Features);
        Assert.IsTrue(codeProfileInfo.CanImportAccessCode);
        Assert.IsTrue(codeProfileInfo.CanViewAccessCode);

        // an operator that sells no codes closes both, whatever the build can do
        token.ClientPolicies = [new ClientPolicy { ClientCountries = ["*"], Normal = 10, PremiumByCode = false }];
        var noCodeInfo = codeApp.VpnProfileService.ImportAccessKey(token.ToAccessKey()).ToInfo(codeApp.Features);
        Assert.IsFalse(noCodeInfo.CanImportAccessCode);
        Assert.IsFalse(noCodeInfo.CanViewAccessCode, "there is no code of theirs to read");
    }

    [TestMethod]
    [DoNotParallelize] // mutates the process-global AppRegionInfo
    public async Task ClientPolicy_sold_premium_is_ignored_by_a_build_without_premium()
    {
        using var accessManager = TestHelper.CreateAccessManager();

        // a CLIENT-like build: no premium tier at all, so it IS the full app and sells nothing —
        // however hard the operator's policy advertises ways to buy one
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.Premium = null;
        await using var app = TestAppHelper.CreateClientApp(appOptions);

        var token = CreateToken();
        token.IsPublic = true;
        // ~#premium: the location serves both tiers, so the free plan and the trial both apply to it
        token.ServerToken.ServerLocations = ["US/California [~#premium]"];
        token.ClientPolicies = [
            new ClientPolicy {
                ClientCountries = ["*"],
                Normal = 10,
                PremiumByTrial = 30,
                PremiumByPurchase = true,
                PremiumByCode = true,
                PurchaseUrl = new Uri("http://localhost/shop")
            }
        ];

        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.VpnProfileId = vpnProfile.VpnProfileId;
        var options = vpnProfile.ToInfo(app.Features).SelectedLocationInfo?.Options
                      ?? throw new InvalidOperationException("No selected location.");

        // every SOLD route is gone; what the server gives away is not the tier's business
        Assert.IsFalse(options.PremiumByPurchase);
        Assert.IsFalse(options.PremiumByCode);
        Assert.IsFalse(options.CanGoPremium, "nothing may draw a Go Premium button");
        Assert.AreEqual(10, options.Normal, "the free plan is the server's business, not the tier's");
        Assert.AreEqual(30, options.PremiumByTrial,
            "a granted premium session costs nothing and passes through no store");
        Assert.IsTrue(options.Prompt, "so the plan chooser still has something to offer");

        // and the purchase page, should anything still reach it, has nothing on it
        var purchaseOptions = await app.GetPurchaseOptions(vpnProfile.VpnProfileId, TestCt);
        Assert.IsNull(purchaseOptions.PurchaseUrl);
        Assert.IsFalse(purchaseOptions.CanGoPremiumByCode);
        Assert.IsFalse(purchaseOptions.IsStoreAvailable);
        Assert.IsNull(purchaseOptions.StoreError, "a build that sells nothing has no store to fail");
    }
}
