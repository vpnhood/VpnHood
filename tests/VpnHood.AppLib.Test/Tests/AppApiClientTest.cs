using System.Net;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.AppLib.Api.HttpClients;
using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Toolkit.Exceptions;
using VpnHood.Net.Toolkit.Utils;
// ReSharper disable ShortLivedHttpClient

namespace VpnHood.AppLib.Test.Tests;

// The HTTP client of the web host's API, against the local web host itself: what a paired browser
// dials, read back into the same DTOs the in-process controllers hand a UI on the device.
[TestClass]
[DoNotParallelize] // the web hosts follow the process-wide AppUiContext
public class AppApiClientTest : TestAppBase
{
    private static Token CreateToken()
    {
        var randomId = Guid.NewGuid();
        return new Token {
            Name = "Default Test Server",
            IssuedAt = DateTime.UtcNow,
            SupportId = "1",
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
    }

    [TestMethod]
    public async Task Http_client_reads_and_writes_through_the_web_host()
    {
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.WebHostFactory = new VpnHoodAppWebHostFactory();
        appOptions.WebRootZipAsset = TestAppHelper.CreateWebRootZip("web-root-test");
        var token = CreateToken();
        appOptions.AccessKeys = [token.ToAccessKey()];
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        var localHost = app.LocalWebHost ?? throw new InvalidOperationException("The app has no local web host.");
        var localUrl = await localHost.EnsureStarted(CancellationToken.None);
        // the address without the web view's cache-buster: the client builds its own paths on it
        using var http = new HttpClient { BaseAddress = new Uri(localUrl.GetLeftPart(UriPartial.Authority) + "/") };
        var api = VpnHoodApiHttpFactory.Create(http);

        // the configuration, whole: the features, the state, the settings, the profiles, the languages
        var config = await api.App.Configure(new ConfigParams { AvailableCultures = ["en", "fa"] }, CancellationToken.None);
        Assert.AreEqual(app.Features.AppId, config.Features.AppId);
        Assert.IsFalse(config.IsRemote, "loopback is the app's own web view");
        Assert.AreEqual(1, config.ClientProfileInfos.Count);
        Assert.AreEqual(token.TokenId, config.ClientProfileInfos[0].TokenId);
        CollectionAssert.AreEqual(new[] { "en", "fa" }, config.AvailableCultureInfos.Select(x => x.Code).ToArray());
        Assert.AreEqual(app.State.ConnectionState, config.State.ConnectionState);

        // the settings written as one and read back by the app
        var settings = config.UserSettings;
        settings.CultureCode = "fa";
        await api.App.SetUserSettings(settings, CancellationToken.None);
        Assert.AreEqual("fa", app.UserSettings.CultureCode);
        var state = await api.App.GetState(CancellationToken.None);
        Assert.AreEqual("fa", state.CurrentUiCultureInfo.Code);

        // a profile, through its own controller
        var profileId = config.ClientProfileInfos[0].ClientProfileId;
        var profile = await api.ClientProfiles.Get(profileId, CancellationToken.None);
        Assert.AreEqual(token.Name, profile.ClientProfileName);
        var renamed = await api.ClientProfiles.Update(profileId,
            new ClientProfileUpdateParams { ClientProfileName = new Patch<string?>("Renamed") }, CancellationToken.None);
        Assert.AreEqual("Renamed", renamed.ClientProfileName);

        // a split list
        await api.App.SetSplitDomains(new SplitDomains { Excludes = "example.com", Includes = "", Blocks = "" }, CancellationToken.None);
        var domains = await api.App.GetSplitDomains(CancellationToken.None);
        Assert.AreEqual("example.com", domains.Excludes);

        // nothing to say is an empty reply, not a failure
        Assert.IsNull(await api.Account.Get(CancellationToken.None));
        Assert.IsNull(await api.ProxyEndPoints.GetDevice(CancellationToken.None));

        // the two replies that are not JSON: the log as text, the promotion image as bytes - while
        // the access code IS json, so a quoted empty string must come back as an empty one
        Assert.IsNotNull(await api.App.Log(CancellationToken.None));
        Assert.AreEqual(string.Empty, await api.ClientProfiles.GetAccessCode(profileId, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<NotExistsException>(() => api.App.PromotionImage(CancellationToken.None));

        // a failure the server reported comes back as the exception it names
        await Assert.ThrowsExactlyAsync<NotExistsException>(() => api.ClientProfiles.Get(Guid.NewGuid(), CancellationToken.None));
    }
}
