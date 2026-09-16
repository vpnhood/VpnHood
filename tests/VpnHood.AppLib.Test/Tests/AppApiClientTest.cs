using System.IO.Compression;
using System.Net;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.WebServer;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Client;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Utils;
// ReSharper disable ShortLivedHttpClient

namespace VpnHood.AppLib.Test.Tests;

// The HTTP client of the web server's API, against the web server itself: what a paired browser
// dials, read back into the same DTOs the in-process controllers hand a UI on the device.
[TestClass]
[DoNotParallelize] // the web server registers as the process-wide singleton
public class AppApiClientTest : TestAppBase
{
    // The smallest SPA the server will serve.
    private static byte[] BuildSpaZip()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true)) {
            using var writer = new StreamWriter(archive.CreateEntry("index.html").Open());
            writer.Write("<html><title>spa-test</title></html>");
        }

        return memoryStream.ToArray();
    }

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
    public async Task Http_client_reads_and_writes_through_the_web_server()
    {
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.Resources.SpaZipData = BuildSpaZip();
        var token = CreateToken();
        appOptions.AccessKeys = [token.ToAccessKey()];
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        using var webServer = VpnHoodAppWebServer.Init(app);
        using var http = new HttpClient { BaseAddress = webServer.Url };
        var api = HttpAppApi.Create(http);

        // the configuration, whole: the features, the state, the settings, the profiles, the languages
        var config = await api.App.Configure(new ConfigParams { AvailableCultures = ["en", "fa"] }, CancellationToken.None);
        Assert.AreEqual(app.Features.AppId, config.Features.AppId);
        Assert.IsFalse(config.IsRemote, "loopback is the app's own web view");
        Assert.AreEqual(1, config.ClientProfileInfos.Length);
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

        // a failure the server reported comes back as the exception it names
        await Assert.ThrowsExactlyAsync<NotExistsException>(() => api.ClientProfiles.Get(Guid.NewGuid(), CancellationToken.None));
    }
}
