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
        using var http = new HttpClient();
        Assert.IsFalse(webServer.IsRemoteAccessActive);

        // start: LAN addresses only, all on one port beside the web view's own; the same answer
        // reaches the SPA through the state it polls
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
        }

        // bound to every interface, so loopback on the remote port reaches it as well as the LAN address
        var remoteLoopbackUrl = new Uri($"http://127.0.0.1:{urls[0].Port}/");
        StringAssert.Contains(await http.GetStringAsync(remoteLoopbackUrl), "spa-test");
        StringAssert.Contains(await http.GetStringAsync(urls[0]), "spa-test");
        using var stateResponse = await http.GetAsync(new Uri(urls[0], "api/app/state"));
        Assert.AreEqual(HttpStatusCode.OK, stateResponse.StatusCode);
        Assert.IsFalse(stateResponse.Headers.Contains("Access-Control-Allow-Origin"), "no Origin, no CORS");

        // an origin off the localhost list gets nothing on a release build
        using var strangerRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(urls[0], "api/app/state"));
        strangerRequest.Headers.Add("Origin", "http://stranger.example");
        using var strangerResponse = await http.SendAsync(strangerRequest);
        Assert.IsFalse(strangerResponse.Headers.Contains("Access-Control-Allow-Origin"), "unknown origin, no CORS");

        // an allowed origin is echoed back as itself, never as a list or "*"
        using var corsRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(urls[0], "api/app/state"));
        corsRequest.Headers.Add("Origin", "http://localhost:8080");
        using var corsResponse = await http.SendAsync(corsRequest);
        Assert.AreEqual("http://localhost:8080", corsResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.AreEqual("Origin", corsResponse.Headers.Vary.Single());

        // the phone knows it is remote, the web view knows it is not, and only the web view may
        // touch the pairing
        StringAssert.Contains(await http.GetStringAsync(new Uri(urls[0], "api/app/config")), "\"isRemote\":true");
        StringAssert.Contains(await http.GetStringAsync(new Uri(webServer.Url, "api/app/config")), "\"isRemote\":false");
        using var remoteStart = await http.PostAsync(new Uri(urls[0], "api/app/remote-access/start"), null);
        Assert.AreEqual(HttpStatusCode.Forbidden, remoteStart.StatusCode);
        using var remoteStop = await http.PostAsync(new Uri(urls[0], "api/app/remote-access/stop"), null);
        Assert.AreEqual(HttpStatusCode.Forbidden, remoteStop.StatusCode);
        Assert.IsTrue(webServer.IsRemoteAccessActive);

        // presence: the requests above came in from the LAN address, so that device shows as connected
        CollectionAssert.Contains(webServer.RemoteAccessState.ConnectedDevices, IPAddress.Parse(urls[0].Host));

        // a repeat start keeps the listener where it is
        var urlsAgain = (await webServer.StartRemoteAccess()).Urls;
        Assert.AreEqual(urls[0], urlsAgain[0]);

        // stop: the remote port is gone, the web view's own listener is not
        webServer.StopRemoteAccess();
        Assert.IsFalse(webServer.IsRemoteAccessActive);
        Assert.AreEqual(0, webServer.RemoteAccessState.Urls.Count);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => http.GetStringAsync(remoteLoopbackUrl));
        StringAssert.Contains(await http.GetStringAsync(webServer.Url), "spa-test");

        // recovery heals only a listener that is held: the resume probe brings nothing back after a stop
        await webServer.RestartIfUnreachable();
        Assert.IsFalse(webServer.IsRemoteAccessActive);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => http.GetStringAsync(remoteLoopbackUrl));

        // the UI context going away is a stop too
        await webServer.StartRemoteAccess();
        Assert.IsTrue(webServer.IsRemoteAccessActive);
        AppUiContext.Context = null;
        Assert.IsFalse(webServer.IsRemoteAccessActive);
    }

    [TestMethod]
    public async Task Remote_access_is_the_primary_listener_for_a_developer()
    {
        // a debug build: the primary is already on every interface for the whole process, so the
        // screen gets its addresses, holds nothing, and closing it changes nothing
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
        foreach (var url in urls)
            Assert.AreEqual(webServer.Url.Port, url.Port);
        StringAssert.Contains(await http.GetStringAsync(urls[0]), "spa-test");

        // any origin is welcome, echoed as itself
        using var corsRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(urls[0], "api/app/state"));
        corsRequest.Headers.Add("Origin", "http://dev.example:5173");
        using var corsResponse = await http.SendAsync(corsRequest);
        Assert.AreEqual("http://dev.example:5173", corsResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        webServer.StopRemoteAccess();
        Assert.IsTrue(webServer.IsRemoteAccessActive);
        Assert.IsTrue(webServer.RemoteAccessState.IsAlwaysOn);
        StringAssert.Contains(await http.GetStringAsync(urls[0]), "spa-test");
    }
}
