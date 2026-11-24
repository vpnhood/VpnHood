using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VpnHood.AppLogic.Api.WebHost.Helpers;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Providers;
using VpnHood.Test.Providers.Proxies;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace VpnHood.Test.Tests.Proxies;

[TestClass]
public class ProxyAutoUpdaterTest : ProxyTestBase
{
    // The list's auto-update downloads at once when it starts, and again only for a new url or interval: the
    // options an app sends again unchanged, at every settings save, download nothing.
    [TestMethod]
    public async Task Auto_update_downloads_again_only_for_a_new_url_or_interval()
    {
        // a list server on a free port, counting each path's downloads
        var downloads = new ConcurrentDictionary<string, int>();
        var port = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback).Port;
        var webServer = new WebserverLite(new WebserverSettings(IPAddress.Loopback.ToString(), port), async ctx => {
            downloads.AddOrUpdate(ctx.Request.Url.RawWithoutQuery, 1, (_, count) => count + 1);
            await ctx.Response.Send("socks5://127.0.0.1:1080");
        });

        _ = webServer.StartAsync(TestCt);
        try {
            await using var connector =
                await ManagedProxyConnector.Create(CreateAutoUpdateOptions(port, "/a"), await CreateStore(),
                    socketFactory: null, serverCheckTimeout: null, TestCt);

            await AssertEqualsWait(1, () => downloads.GetValueOrDefault("/a"), "the first download");

            // the same options again: no download
            await connector.UpdateOptions(CreateAutoUpdateOptions(port, "/a"), TestCt);
            await Task.Delay(TimeSpan.FromSeconds(4), TestCt);
            Assert.AreEqual(1, downloads.GetValueOrDefault("/a"), "no download for the same options");

            // a new url: a download at once
            await connector.UpdateOptions(CreateAutoUpdateOptions(port, "/b"), TestCt);
            await AssertEqualsWait(1, () => downloads.GetValueOrDefault("/b"), "a download for the new url");
        }
        finally {
            webServer.TryStop();
        }
    }

    // A list with no endpoint yet, before its first download: the VPN went straight to its server, around the proxies
    // the user had turned on. A connection now waits for the list, held back here, and goes through its proxy; one
    // whose caller gives up meanwhile ends with its cancellation
    [TestMethod]
    public async Task A_connection_to_a_list_with_no_endpoint_yet_waits_for_its_first_download()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var targetEndPoint = (IPEndPoint)target.LocalEndpoint;
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer($"socks5://{proxy.EndPoint}", isHeld: true, TestCt);

        // an hour between downloads: the first one at once all the same, not at the job runner's next check
        await using var connector = await ManagedProxyConnector.Create(CreateListOptions(listServer.Url,
            TimeSpan.FromHours(1)), await CreateStore(), socketFactory: null, serverCheckTimeout: null, TestCt);

        Assert.IsFalse(connector.IsEnabled, "The list has an endpoint before its download.");
        using var givingUpCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var givingUp = connector.ConnectAsync(new TestSocketFactory(), targetEndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, givingUpCts.Token);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), targetEndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => listServer.DownloadCount, "The list's first download did not start at once.");
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
        Assert.IsFalse(connecting.IsCompleted, "A connection went on before the list came.");

        await givingUpCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => givingUp);

        listServer.Release();
        using var connection = await connecting;
        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint,
            "The connection went around the list's proxy.");
    }

    // With the update interval "never", the app's default, nothing downloaded the list, and a connection to it, empty,
    // failed every time. Its first download now comes all the same, as the app leaves it to the connector
    [TestMethod]
    public async Task A_list_with_no_update_interval_still_has_its_first_download()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer($"socks5://{proxy.EndPoint}", isHeld: false, TestCt);
        await using var connector = await ManagedProxyConnector.Create(CreateListOptions(listServer.Url,
            interval: null), await CreateStore(), socketFactory: null, serverCheckTimeout: null, TestCt);

        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
        Assert.AreEqual(1, listServer.DownloadCount);
    }

    // A list whose endpoints are all turned off has none to use either: it waits for its download as an empty one does,
    // where it failed at once
    [TestMethod]
    public async Task A_list_with_every_endpoint_turned_off_waits_for_its_first_download()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer($"socks5://{proxy.EndPoint}", isHeld: true, TestCt);
        await using var connector = await ManagedProxyConnector.Create(
            CreateListOptions(listServer.Url, TimeSpan.FromHours(1)),
            await CreateStore(ToProxyEndPoint(new IPEndPoint(IPAddress.Loopback, 1), isEnabled: false)),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => listServer.DownloadCount);
        Assert.IsFalse(connecting.IsCompleted, "The connection failed before the list came.");
        listServer.Release();
        using var connection = await connecting;
        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
    }

    // New options during the wait: the old download's end let the waiting connections into the empty list, which then
    // failed while the new one was coming. They now wait for the new one
    [TestMethod]
    public async Task A_connection_waiting_for_a_list_follows_its_replacement()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var oldListServer = new ListServer("never sent", isHeld: true, TestCt);
        using var newListServer = new ListServer($"socks5://{proxy.EndPoint}", isHeld: true, TestCt);
        await using var connector = await ManagedProxyConnector.Create(
            CreateListOptions(oldListServer.Url, TimeSpan.FromHours(1)), await CreateStore(), socketFactory: null,
            serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => oldListServer.DownloadCount);
        await connector.UpdateOptions(CreateListOptions(newListServer.Url, TimeSpan.FromHours(1)), TestCt);
        await AssertEqualsWait(1, () => newListServer.DownloadCount);
        Assert.IsFalse(connecting.IsCompleted, "The connection failed before the new list came.");

        newListServer.Release();
        using var connection = await connecting;
        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
    }

    // A connection waiting for the list goes on with an endpoint the app adds meanwhile, with the options it sends then
    [TestMethod]
    public async Task A_connection_waiting_for_the_list_takes_an_endpoint_the_app_adds()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer("never sent", isHeld: true, TestCt);
        var proxyOptions = CreateListOptions(listServer.Url, TimeSpan.FromHours(1));
        await using var connector = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => listServer.DownloadCount);
        using var appStore = await CreateStore(ToProxyEndPoint(proxy.EndPoint));
        await connector.UpdateOptions(proxyOptions, TestCt);

        using var connection = await connecting.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
    }

    // A reload that brought an enabled endpoint marked the list's download as done, so a connection made after the
    // endpoint was turned off again failed at once, while the list was still coming. A reload now only wakes the
    // waiting connections, which look again; and the endpoint's going off started the download still coming again,
    // which the gate's one release then let through a second time
    [TestMethod]
    public async Task A_connection_still_waits_for_the_list_after_an_endpoint_came_and_went()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer($"socks5://{proxy.EndPoint}", isHeld: true, TestCt);
        var proxyOptions = CreateListOptions(listServer.Url, TimeSpan.FromHours(1));
        await using var connector = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        await AssertEqualsWait(1, () => listServer.DownloadCount);

        // the app adds an endpoint, then turns it off, and sends its options each time
        var appEndPoint = new IPEndPoint(IPAddress.Loopback, 1);
        using var appStore = await CreateStore(ToProxyEndPoint(appEndPoint));
        await connector.UpdateOptions(proxyOptions, TestCt);
        await Upsert(appStore, [new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(appEndPoint, isEnabled: false) }]);
        await connector.UpdateOptions(proxyOptions, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
        Assert.IsFalse(connecting.IsCompleted, "The connection ended before the list came.");
        listServer.Release();
        using var connection = await connecting;
        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
        Assert.AreEqual(1, listServer.DownloadCount, "The download still coming was started again.");
    }

    // A list with no update interval whose last enabled endpoint the app turns off has nothing to use: it downloads the
    // list once, as at its start, where it did nothing
    [TestMethod]
    public async Task A_list_with_no_interval_whose_last_endpoint_goes_off_downloads_again()
    {
        using var listServer = new ListServer("socks5://127.0.0.1:1080", isHeld: false, TestCt);
        var proxyOptions = CreateListOptions(listServer.Url, interval: null);
        var storedEndPoint = new IPEndPoint(IPAddress.Loopback, 1);
        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(storedEndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
        Assert.AreEqual(0, listServer.DownloadCount, "A list with an endpoint to use and no interval was downloaded.");

        using var appStore = await CreateStore();
        await Upsert(appStore,
            [new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(storedEndPoint, isEnabled: false) }]);
        await connector.UpdateOptions(proxyOptions, TestCt);
        await AssertEqualsWait(1, () => listServer.DownloadCount, "The list was not downloaded again.");
    }

    // A list with no update interval downloaded once, at its start: once the user turned off the endpoint it brought,
    // nothing was left to use, and the one download, done, kept any other from starting
    [TestMethod]
    public async Task A_list_with_no_interval_downloads_again_when_its_downloaded_endpoint_goes_off()
    {
        using var listServer = new ListServer("socks5://127.0.0.1:1080", isHeld: false, TestCt);
        var proxyOptions = CreateListOptions(listServer.Url, interval: null);
        await using var connector = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        await AssertEqualsWait(true, () => connector.IsEnabled, "The list did not come.");
        using var appStore = await CreateStore();
        await Upsert(appStore, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(new IPEndPoint(IPAddress.Loopback, 1080), isEnabled: false)
            }
        ]);

        await connector.UpdateOptions(proxyOptions, TestCt);
        await AssertEqualsWait(2, () => listServer.DownloadCount, "The list was not downloaded again.");
    }

    [TestMethod]
    public async Task The_close_ends_a_connection_waiting_for_the_list()
    {
        using var listServer = new ListServer("never sent", isHeld: true, TestCt);
        var connector = await ManagedProxyConnector.Create(CreateListOptions(listServer.Url, TimeSpan.FromHours(1)),
            await CreateStore(), socketFactory: null, serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), new IPEndPoint(IPAddress.Loopback, 443),
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => listServer.DownloadCount);
        await connector.DisposeAsync();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connecting);
    }

    // Under the kill switch's block, which lets only the VPN's own traffic out, a download over the system's sockets
    // and resolver could not reach the list: it now goes over the VPN's socket factory
    [TestMethod]
    public async Task The_lists_download_goes_over_the_VPNs_socket_factory()
    {
        using var listServer = new ListServer("socks5://127.0.0.1:1080", isHeld: false, TestCt);
        var socketFactory = new EndPointRecordingSocketFactory();
        await using var connector = await ManagedProxyConnector.Create(CreateListOptions(listServer.Url,
            TimeSpan.FromHours(1)), await CreateStore(), socketFactory, serverCheckTimeout: null, TestCt);

        await connector.WaitForEndPoints(TestCt);

        Assert.IsTrue(connector.IsEnabled, "The list did not come.");
        Assert.Contains(new IPEndPoint(IPAddress.Loopback, listServer.Port), socketFactory.EndPoints,
            "The download went around the socket factory.");
    }

    // The update's later downloads come once the tunnel is up, and go the platform's way, not over the VPN's sockets
    [TestMethod]
    public async Task The_lists_later_downloads_leave_the_VPNs_socket_factory_alone()
    {
        using var listServer = new ListServer("socks5://127.0.0.1:1080", isHeld: false, TestCt);
        var socketFactory = new EndPointRecordingSocketFactory();
        await using var connector = await ManagedProxyConnector.Create(CreateListOptions(listServer.Url,
            TimeSpan.FromSeconds(1)), await CreateStore(), socketFactory, serverCheckTimeout: null, TestCt);

        await AssertEqualsWait(true, () => listServer.DownloadCount >= 2, "The list was not downloaded again.",
            timeout: 15000);

        Assert.AreEqual(1, socketFactory.EndPoints.Count(x => x.Port == listServer.Port),
            "A later download went over the socket factory.");
    }

    // The VPN's sockets, kept since the connector's start for its first download, served a list whose URL the app's
    // options gave once the session was up, and its download went around the tunnel: they serve the update the
    // connector starts with alone
    [TestMethod]
    public async Task A_list_the_options_bring_later_leaves_the_VPNs_socket_factory_alone()
    {
        using var listServer = new ListServer("socks5://127.0.0.1:1080", isHeld: false, TestCt);
        var socketFactory = new EndPointRecordingSocketFactory();
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(ToProxyEndPoint(new IPEndPoint(IPAddress.Loopback, 1081))), socketFactory,
            serverCheckTimeout: null, TestCt);

        await connector.UpdateOptions(CreateListOptions(listServer.Url, TimeSpan.FromHours(1)), TestCt);
        await AssertEqualsWait(1, () => listServer.DownloadCount);
        Assert.IsEmpty(socketFactory.EndPoints, "The download went over the socket factory.");
    }

    private static ProxyOptions CreateListOptions(Uri url, TimeSpan? interval)
    {
        return new ProxyOptions {
            VerifyTls = false, // the targets speak no TLS
            AutoUpdateOptions = new ProxyAutoUpdateOptions { Url = url, Interval = interval }
        };
    }

    // nine seconds between downloads, the first one at once
    private static ProxyOptions CreateAutoUpdateOptions(int port, string path)
    {
        return new ProxyOptions {
            AutoUpdateOptions = new ProxyAutoUpdateOptions {
                Url = new Uri($"http://127.0.0.1:{port}{path}"),
                Interval = TimeSpan.FromSeconds(9)
            }
        };
    }
}
