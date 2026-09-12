using System.IO.Compression;
using System.Net;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
[DoNotParallelize] // the web server registers as the process-wide singleton, and remote access follows the process-wide AppUiContext
public class WebServerTest : TestAppBase
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

    [TestMethod]
    public async Task Remote_access_lives_between_start_and_stop()
    {
        // a release build: remote access exists only while a screen holds it
        var appOptions = TestAppHelper.CreateAppOptions(isDebugMode: false);
        appOptions.Resources.SpaZipData = BuildSpaZip();
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        using var webServer = VpnHoodAppWebServer.Init(app);
        // no redirects and no cookie jar: the pairing handshake is asserted step by step
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        Assert.IsFalse(webServer.IsRemoteAccessActive);

        // start: one listener per LAN address on one port beside the web view's own, every
        // address carrying the pairing token; the same answer reaches the SPA through the state it polls
        var remoteAccess = await webServer.StartRemoteAccess();
        var urls = remoteAccess.Urls;
        Assert.IsTrue(remoteAccess.IsActive);
        Assert.IsFalse(remoteAccess.IsAlwaysOn);
        Assert.IsTrue(webServer.IsRemoteAccessActive);
        Assert.AreEqual(urls.Count, webServer.RemoteAccessState.Urls.Count);
        Assert.AreNotEqual(0, urls.Count, "the machine running the test has no LAN address");
        foreach (var url in urls) {
            Assert.IsFalse(IPAddress.IsLoopback(IPAddress.Parse(url.Host)), url.ToString());
            Assert.AreNotEqual(webServer.Url.Port, url.Port);
            StringAssert.StartsWith(url.Query, "?pair=");
        }

        var pairUrl = urls[0];
        var token = pairUrl.Query["?pair=".Length..];
        var root = new Uri($"http://{pairUrl.Host}:{pairUrl.Port}/");

        // bound to the address itself, never 0.0.0.0: loopback on the remote port reaches nothing
        var remoteLoopbackUrl = new Uri($"http://127.0.0.1:{root.Port}/");
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => http.GetStringAsync(remoteLoopbackUrl));

        // without the pairing there is only the hint page, for the SPA and for the API alike
        using var unpaired = await http.GetAsync(root);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unpaired.StatusCode);
        StringAssert.Contains(await unpaired.Content.ReadAsStringAsync(), "Scan the code");
        using var unpairedApi = await http.GetAsync(new Uri(root, "api/app/state"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, unpairedApi.StatusCode);
        using var wrongToken = await http.GetAsync(new Uri(root, "?pair=nope"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
        Assert.AreEqual(0, webServer.RemoteAccessState.ConnectedDevices.Length, "a knock is not presence");

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

        // paired: the SPA and the API answer
        StringAssert.Contains(await http.GetStringAsync(root), "spa-test");
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

        // the phone knows it is remote, the web view knows it is not, and only the web view may
        // touch the pairing
        StringAssert.Contains(await http.GetStringAsync(new Uri(root, "api/app/config")), "\"isRemote\":true");
        StringAssert.Contains(await http.GetStringAsync(new Uri(webServer.Url, "api/app/config")), "\"isRemote\":false");

        // the web view's own listener is gated too. CORS hides a reply from another site but does
        // not stop the request arriving, and these routes take their parameters in the query string,
        // so an unknown Origin is refused rather than obeyed.
        using var localCsrf = new HttpRequestMessage(HttpMethod.Post, new Uri(webServer.Url, "api/app/disconnect"));
        localCsrf.Headers.Add("Origin", "http://evil.example");
        using var localCsrfResponse = await http.SendAsync(localCsrf);
        Assert.AreEqual(HttpStatusCode.Forbidden, localCsrfResponse.StatusCode);

        // and a stranger's name in Host cannot walk a rebound browser in
        using var localRebound = new HttpRequestMessage(HttpMethod.Get, new Uri(webServer.Url, "api/app/state"));
        localRebound.Headers.Host = "evil.example";
        using var localReboundResponse = await http.SendAsync(localRebound);
        Assert.AreEqual(HttpStatusCode.Forbidden, localReboundResponse.StatusCode);

        // what still passes: the app's own page, and the loopback listener dialled by name, which
        // is what the SPA's dev server does
        using var localSelf = new HttpRequestMessage(HttpMethod.Get, new Uri(webServer.Url, "api/app/state"));
        localSelf.Headers.Add("Origin", $"http://{webServer.Url.Authority}");
        using var localSelfResponse = await http.SendAsync(localSelf);
        Assert.AreEqual(HttpStatusCode.OK, localSelfResponse.StatusCode);

        using var byName = new HttpRequestMessage(HttpMethod.Get, new Uri(webServer.Url, "api/app/state"));
        byName.Headers.Host = $"localhost:{webServer.Url.Port}";
        using var byNameResponse = await http.SendAsync(byName);
        Assert.AreEqual(HttpStatusCode.OK, byNameResponse.StatusCode);
        using var remoteStart = await http.PostAsync(new Uri(root, "api/app/remote-access/start"), null);
        Assert.AreEqual(HttpStatusCode.Forbidden, remoteStart.StatusCode);
        using var remoteStop = await http.PostAsync(new Uri(root, "api/app/remote-access/stop"), null);
        Assert.AreEqual(HttpStatusCode.Forbidden, remoteStop.StatusCode);
        Assert.IsTrue(webServer.IsRemoteAccessActive);

        // presence: the paired requests above came in from the LAN address, so that device shows as connected
        CollectionAssert.Contains(webServer.RemoteAccessState.ConnectedDevices, IPAddress.Parse(root.Host));

        // a repeat start and a refresh keep the listeners, the port and the token where they are
        Assert.AreEqual(pairUrl, (await webServer.StartRemoteAccess()).Urls[0]);
        Assert.AreEqual(pairUrl, (await webServer.RefreshRemoteAccess()).Urls[0]);

        // stop: the remote port is gone, the web view's own listener is not, and a refresh starts nothing
        webServer.StopRemoteAccess();
        Assert.IsFalse(webServer.IsRemoteAccessActive);
        Assert.AreEqual(0, webServer.RemoteAccessState.Urls.Count);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => http.GetStringAsync(root));
        StringAssert.Contains(await http.GetStringAsync(webServer.Url), "spa-test");
        Assert.IsFalse((await webServer.RefreshRemoteAccess()).IsActive);
        Assert.IsFalse(webServer.IsRemoteAccessActive);

        // recovery heals only a listener that is held: the resume probe brings nothing back after a stop
        await webServer.RestartIfUnreachable();
        Assert.IsFalse(webServer.IsRemoteAccessActive);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => http.GetStringAsync(root));

        // a new start is a new pairing: the old cookie opens nothing
        var second = await webServer.StartRemoteAccess();
        Assert.IsTrue(webServer.IsRemoteAccessActive);
        Assert.AreNotEqual(pairUrl.Query, second.Urls[0].Query);
        using var staleCookie = await http.GetAsync(root);
        Assert.AreEqual(HttpStatusCode.Unauthorized, staleCookie.StatusCode);

        // the UI context going away is a stop too
        AppUiContext.Context = null;
        Assert.IsFalse(webServer.IsRemoteAccessActive);
    }

    [TestMethod]
    public async Task Remote_access_is_always_on_for_a_developer()
    {
        // a debug build: the LAN listeners come up by themselves at Init on the web view's own
        // port and ask for no pairing, so the screen holds nothing and closing it changes nothing
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.Resources.SpaZipData = BuildSpaZip();
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        using var webServer = VpnHoodAppWebServer.Init(app);
        using var http = new HttpClient();
        Assert.IsTrue(webServer.IsRemoteAccessActive);

        var remoteAccess = await webServer.StartRemoteAccess();
        Assert.IsTrue(remoteAccess.IsAlwaysOn);
        var urls = remoteAccess.Urls;
        Assert.AreNotEqual(0, urls.Count, "the machine running the test has no LAN address");
        foreach (var url in urls) {
            Assert.AreEqual(webServer.Url.Port, url.Port);
            Assert.AreEqual(string.Empty, url.Query, "no pairing for a developer");
        }

        // a bare address answers, and the web view's own listener is still loopback
        StringAssert.Contains(await http.GetStringAsync(urls[0]), "spa-test");
        StringAssert.Contains(await http.GetStringAsync(webServer.Url), "spa-test");

        // any origin is welcome, echoed as itself, and passes the gate for the same reason: a
        // developer's build is deliberately open to whatever port their dev server is on
        using var corsRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(urls[0], "api/app/state"));
        corsRequest.Headers.Add("Origin", "http://dev.example:5173");
        using var corsResponse = await http.SendAsync(corsRequest);
        Assert.AreEqual("http://dev.example:5173", corsResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        // a stranger's name in Host is still refused
        using var rebound = new HttpRequestMessage(HttpMethod.Get, new Uri(urls[0], "api/app/state"));
        rebound.Headers.Host = "stranger.example";
        using var reboundResponse = await http.SendAsync(rebound);
        Assert.AreEqual(HttpStatusCode.Forbidden, reboundResponse.StatusCode);

        webServer.StopRemoteAccess();
        Assert.IsTrue(webServer.IsRemoteAccessActive);
        Assert.IsTrue(webServer.RemoteAccessState.IsAlwaysOn);
        StringAssert.Contains(await http.GetStringAsync(urls[0]), "spa-test");
    }
}
