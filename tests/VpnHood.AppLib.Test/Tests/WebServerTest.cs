using System.Net;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.AppLib.WebHosting;
using VpnHood.Core.Client.Devices.UiContexts;
// ReSharper disable ShortLivedHttpClient

namespace VpnHood.AppLib.Test.Tests;

// The two web hosts of one app, as a head gets them: the local one the app's own web view loads, and
// the remote one a phone pairs with. What each binds, what it asks of a caller, and who ends it.
[TestClass]
[DoNotParallelize] // remote access follows the process-wide AppUiContext
public class WebServerTest : TestAppBase
{
    private const string PageTitle = "web-root-test";

    private AppOptions CreateWebHostOptions(bool isDebugMode)
    {
        var appOptions = TestAppHelper.CreateAppOptions(isDebugMode);
        appOptions.WebHostFactory = new VpnHoodAppWebHostFactory();
        appOptions.WebRootZipAsset = TestAppHelper.CreateWebRootZip(PageTitle);
        return appOptions;
    }

    private static IAppWebHost RequireHost(IAppWebHost? host, string name)
    {
        return host ?? throw new InvalidOperationException($"The app has no {name} web host: was no factory set?");
    }

    [TestMethod]
    public async Task Remote_access_lives_between_start_and_stop()
    {
        // a release build: remote access exists only while a screen holds it
        var appOptions = CreateWebHostOptions(isDebugMode: false);
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        var local = RequireHost(app.LocalWebHost, "local");
        var remote = RequireHost(app.RemoteWebHost, "remote");
        var localUrl = await local.EnsureStarted(CancellationToken.None);
        // no redirects and no cookie jar: the pairing handshake is asserted step by step
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        Assert.IsTrue(local.IsActive);
        Assert.IsFalse(remote.IsActive);
        Assert.AreEqual(0, remote.Urls.Count, "nothing to dial before a start");

        // start: one listener per LAN address, every address carrying the pairing token; the same
        // answer reaches a UI through the API's remote-access state
        var pairUrl = await remote.EnsureStarted(CancellationToken.None);
        var urls = remote.Urls;
        Assert.IsTrue(remote.IsActive);
        Assert.IsFalse(remote.IsAlwaysOn);
        Assert.AreEqual(pairUrl, urls[0]);
        Assert.AreNotEqual(0, urls.Count, "the machine running the test has no LAN address");
        foreach (var url in urls) {
            Assert.IsFalse(IPAddress.IsLoopback(IPAddress.Parse(url.Host)), url.ToString());
            StringAssert.StartsWith(url.Query, "?pair=");
        }

        var remoteAccess = await app.Api.App.GetRemoteAccess(CancellationToken.None);
        Assert.IsTrue(remoteAccess.IsActive);
        Assert.IsFalse(remoteAccess.IsAlwaysOn);
        CollectionAssert.AreEqual(urls.ToArray(), remoteAccess.Urls.ToArray());

        var token = pairUrl.Query["?pair=".Length..];
        var root = new Uri($"http://{pairUrl.Host}:{pairUrl.Port}/");

        // without the pairing there is only the hint page, for the page and for the API alike
        using var unpaired = await http.GetAsync(root);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unpaired.StatusCode);
        StringAssert.Contains(await unpaired.Content.ReadAsStringAsync(), "Scan the code");
        using var unpairedApi = await http.GetAsync(new Uri(root, "api/app/state"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, unpairedApi.StatusCode);
        using var wrongToken = await http.GetAsync(new Uri(root, "?pair=nope"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
        Assert.AreEqual(0, remote.ConnectedDevices.Count, "a knock is not presence");

        // a native client sends the same token as a bearer header and sees no redirect
        using var bearer = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "api/app/state"));
        bearer.Headers.Add("Authorization", $"Bearer {token}");
        using var bearerResponse = await http.SendAsync(bearer);
        Assert.AreEqual(HttpStatusCode.OK, bearerResponse.StatusCode);
        using var wrongBearer = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "api/app/state"));
        wrongBearer.Headers.Add("Authorization", "Bearer nope");
        using var wrongBearerResponse = await http.SendAsync(wrongBearer);
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrongBearerResponse.StatusCode);

        // the QR's address: the token becomes an HttpOnly cookie and leaves the address bar. Lax,
        // not Strict: the phone arrives from a scanner app, and Strict can be withheld on the
        // redirect that follows that cross-app navigation
        using var paired = await http.GetAsync(pairUrl);
        Assert.AreEqual(HttpStatusCode.Found, paired.StatusCode);
        Assert.AreEqual("/", paired.Headers.Location?.ToString());
        var setCookie = paired.Headers.GetValues("Set-Cookie").Single();
        StringAssert.StartsWith(setCookie, $"vh-pair={token};");
        StringAssert.Contains(setCookie, "HttpOnly");
        StringAssert.Contains(setCookie, "SameSite=Lax");
        http.DefaultRequestHeaders.Add("Cookie", $"vh-pair={token}");

        // paired: the page and the API answer
        StringAssert.Contains(await http.GetStringAsync(root), PageTitle);
        using var stateResponse = await http.GetAsync(new Uri(root, "api/app/state"));
        Assert.AreEqual(HttpStatusCode.OK, stateResponse.StatusCode);
        Assert.IsFalse(stateResponse.Headers.Contains("Access-Control-Allow-Origin"), "no Origin, no CORS");

        // a request under a stranger's name in Host is refused even when paired (DNS rebinding)
        using var rebound = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "api/app/state"));
        rebound.Headers.Host = "stranger.example";
        using var reboundResponse = await http.SendAsync(rebound);
        Assert.AreEqual(HttpStatusCode.Forbidden, reboundResponse.StatusCode);

        // an origin off the localhost list is refused outright on a release build: CORS would hide
        // the reply from it, but the request itself must not be acted on
        using var strangerRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "api/app/state"));
        strangerRequest.Headers.Add("Origin", "http://stranger.example");
        using var strangerResponse = await http.SendAsync(strangerRequest);
        Assert.AreEqual(HttpStatusCode.Forbidden, strangerResponse.StatusCode);
        Assert.IsFalse(strangerResponse.Headers.Contains("Access-Control-Allow-Origin"), "unknown origin, no CORS");

        // the phone's own page: its origin is this listener's own address
        using var phoneSelf = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "api/app/state"));
        phoneSelf.Headers.Add("Origin", $"http://{root.Authority}");
        using var phoneSelfResponse = await http.SendAsync(phoneSelf);
        Assert.AreEqual(HttpStatusCode.OK, phoneSelfResponse.StatusCode);

        // an allowed origin is echoed back as itself, never as a list or "*"
        using var corsRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "api/app/state"));
        corsRequest.Headers.Add("Origin", "http://localhost:8080");
        using var corsResponse = await http.SendAsync(corsRequest);
        Assert.AreEqual("http://localhost:8080", corsResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.AreEqual("Origin", corsResponse.Headers.Vary.Single());

        // the phone knows it is remote, the web view knows it is not
        StringAssert.Contains(await http.GetStringAsync(new Uri(root, "api/app/info")), "\"isRemote\":true");
        StringAssert.Contains(await http.GetStringAsync(new Uri(localUrl, "api/app/info")), "\"isRemote\":false");

        // the web view's own listener is gated too. CORS hides a reply from another site but does
        // not stop the request arriving, and these routes take their parameters in the query string,
        // so an unknown Origin is refused rather than obeyed.
        using var localCsrf = new HttpRequestMessage(HttpMethod.Post, new Uri(localUrl, "api/app/disconnect"));
        localCsrf.Headers.Add("Origin", "http://evil.example");
        using var localCsrfResponse = await http.SendAsync(localCsrf);
        Assert.AreEqual(HttpStatusCode.Forbidden, localCsrfResponse.StatusCode);

        // and a stranger's name in Host cannot walk a rebound browser in
        using var localRebound = new HttpRequestMessage(HttpMethod.Get, new Uri(localUrl, "api/app/state"));
        localRebound.Headers.Host = "evil.example";
        using var localReboundResponse = await http.SendAsync(localRebound);
        Assert.AreEqual(HttpStatusCode.Forbidden, localReboundResponse.StatusCode);

        // what still passes: the app's own page, and the loopback listener dialled by name, which
        // is what a UI's dev server does
        using var localSelf = new HttpRequestMessage(HttpMethod.Get, new Uri(localUrl, "api/app/state"));
        localSelf.Headers.Add("Origin", $"http://{localUrl.Authority}");
        using var localSelfResponse = await http.SendAsync(localSelf);
        Assert.AreEqual(HttpStatusCode.OK, localSelfResponse.StatusCode);

        using var byName = new HttpRequestMessage(HttpMethod.Get, new Uri(localUrl, "api/app/state"));
        byName.Headers.Host = $"localhost:{localUrl.Port}";
        using var byNameResponse = await http.SendAsync(byName);
        Assert.AreEqual(HttpStatusCode.OK, byNameResponse.StatusCode);

        // pairing is the device's own business: a phone that reached the app through it can neither
        // read, end nor start it
        using var remoteRead = await http.GetAsync(new Uri(root, "api/app/remote-access"));
        Assert.AreEqual(HttpStatusCode.Forbidden, remoteRead.StatusCode);
        using var remoteStart = await http.PostAsync(new Uri(root, "api/app/remote-access/start"), null);
        Assert.AreEqual(HttpStatusCode.Forbidden, remoteStart.StatusCode);
        using var remoteStop = await http.PostAsync(new Uri(root, "api/app/remote-access/stop"), null);
        Assert.AreEqual(HttpStatusCode.Forbidden, remoteStop.StatusCode);
        Assert.IsTrue(remote.IsActive);

        // presence: the paired requests above came in from the LAN address, so that device shows as connected
        CollectionAssert.Contains(remote.ConnectedDevices.ToArray(), IPAddress.Parse(root.Host));
        CollectionAssert.Contains((await app.Api.App.GetRemoteAccess(CancellationToken.None)).ConnectedDevices, IPAddress.Parse(root.Host));

        // a repeat start keeps the listeners, the port and the token where they are
        Assert.AreEqual(pairUrl, await remote.EnsureStarted(CancellationToken.None));
        Assert.AreEqual(pairUrl, (await app.Api.App.StartRemoteAccess(CancellationToken.None)).Urls[0]);

        // stop: the remote listeners are gone, the web view's own is not, and a poll starts nothing
        await remote.Stop(CancellationToken.None);
        Assert.IsFalse(remote.IsActive);
        Assert.AreEqual(0, remote.Urls.Count);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => http.GetStringAsync(root));
        StringAssert.Contains(await http.GetStringAsync(localUrl), PageTitle);
        Assert.IsFalse((await app.Api.App.GetRemoteAccess(CancellationToken.None)).IsActive);
        Assert.IsFalse(remote.IsActive);

        // recovery heals only a listener that is held: the resume probe brings nothing back after a stop
        AppUiContext.NotifyResumed();
        await Task.Delay(500);
        Assert.IsFalse(remote.IsActive);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => http.GetStringAsync(root));

        // a new start in the same run is the same pairing, address and token alike: the phone's
        // cookie from before the stop still opens it, so an accidental close costs no rescan
        Assert.AreEqual(pairUrl, await remote.EnsureStarted(CancellationToken.None));
        Assert.IsTrue(remote.IsActive);
        using var keptCookie = await http.GetAsync(new Uri(root, "api/app/state"));
        Assert.AreEqual(HttpStatusCode.OK, keptCookie.StatusCode);

        // the UI context going away is a stop too
        AppUiContext.Context = null;
        Assert.IsFalse(remote.IsActive);
        Assert.IsTrue(local.IsActive, "the local host outlives every UI context: the next one loads from it");
    }

    [TestMethod]
    public async Task Remote_access_is_always_on_for_a_developer()
    {
        // a debug build: the LAN listeners come up by themselves at Init and ask for no pairing, so
        // no screen holds them and closing one changes nothing
        var appOptions = CreateWebHostOptions(isDebugMode: true);
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        var local = RequireHost(app.LocalWebHost, "local");
        var remote = RequireHost(app.RemoteWebHost, "remote");
        using var http = new HttpClient();
        Assert.IsTrue(remote.IsAlwaysOn);
        Assert.IsTrue(remote.IsActive);

        var url = await remote.EnsureStarted(CancellationToken.None);
        var urls = remote.Urls;
        Assert.AreNotEqual(0, urls.Count, "the machine running the test has no LAN address");
        foreach (var address in urls)
            Assert.AreEqual(string.Empty, address.Query, "no pairing for a developer");

        var remoteAccess = await app.Api.App.GetRemoteAccess(CancellationToken.None);
        Assert.IsTrue(remoteAccess.IsAlwaysOn);
        Assert.IsTrue(remoteAccess.IsActive);

        // a bare address answers, and the web view's own listener is still loopback
        StringAssert.Contains(await http.GetStringAsync(url), PageTitle);
        var localUrl = await local.EnsureStarted(CancellationToken.None);
        Assert.IsTrue(IPAddress.IsLoopback(IPAddress.Parse(localUrl.Host)));
        StringAssert.Contains(await http.GetStringAsync(localUrl), PageTitle);

        // any origin is welcome, echoed as itself, and passes the gate for the same reason: a
        // developer's build is deliberately open to whatever port their dev server is on
        using var corsRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(url, "api/app/state"));
        corsRequest.Headers.Add("Origin", "http://dev.example:5173");
        using var corsResponse = await http.SendAsync(corsRequest);
        Assert.AreEqual("http://dev.example:5173", corsResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        // a stranger's name in Host is still refused
        using var rebound = new HttpRequestMessage(HttpMethod.Get, new Uri(url, "api/app/state"));
        rebound.Headers.Host = "stranger.example";
        using var reboundResponse = await http.SendAsync(rebound);
        Assert.AreEqual(HttpStatusCode.Forbidden, reboundResponse.StatusCode);

        // a stop is refused by a host nothing holds
        await remote.Stop(CancellationToken.None);
        Assert.IsTrue(remote.IsActive);
        Assert.IsTrue(remote.IsAlwaysOn);
        StringAssert.Contains(await http.GetStringAsync(url), PageTitle);
    }
}
