using System.Net;
using Ga4.Trackers;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientCloseTest : TestBase
{
    // A close that came while the session was built found no session to close, so the connect published it and
    // started it on the adapter, which a client that keeps its adapter had left up: a closed client with a running
    // session. The connect closes the session and ends
    [TestMethod]
    public async Task A_close_while_the_session_is_built_ends_the_connect()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.AllowAnonymousTracker = true;
        clientOptions.AutoDisposeVpnAdapter = false;
        using var vpnAdapter = new TestNullVpnAdapter();

        // the build's last step tracks the connect, and the tracker closes the client there
        var tracker = new ClosingTracker();
        await using var client = new VpnHoodClient(vpnAdapter, new TestSocketFactory(), TestHelper.ClientNetFilter,
            proxyConnector: null, tracker, Path.Combine(TestHelper.WorkingPath, $"Client_{Guid.NewGuid()}"),
            clientOptions);

        tracker.Client = client;

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.Connect(TestCt));
        Assert.IsFalse(vpnAdapter.IsStarted, "The closed client's session started the adapter.");
        Assert.AreEqual(ClientState.Disposed, client.State);
    }

    // A close that came between the session's last look and its adapter's start found the adapter down and left it, and
    // the start then brought it up, the whole of it after the close: a client that keeps its adapter was left with it
    // running. The close ends the start at its first step
    [TestMethod]
    public async Task A_close_as_the_adapter_starts_leaves_it_down()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.AutoDisposeVpnAdapter = false;
        using var vpnAdapter = new GatedVpnAdapter();
        await using var client = await TestHelper.CreateClient(clientOptions, vpnAdapter: vpnAdapter,
            autoConnect: false);

        var connect = client.Connect(TestCt);
        await vpnAdapter.StartWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        client.Dispose();
        vpnAdapter.OpenGate();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => connect);
        Assert.IsTrue(vpnAdapter.IsStartCanceled, "The close did not end the adapter's start.");
        Assert.IsFalse(vpnAdapter.IsStarted, "The closed client's adapter is up.");
    }

    // A graceful close that came while a quick one, begun by the session's end on another thread, still released the
    // filters and the adapter, ended at once: its caller, the VPN service, published the end and the next connect met
    // what was still being released. It ends with the release
    [TestMethod]
    public async Task A_graceful_close_ends_with_a_quick_close_under_way()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var vpnAdapter = new GatedVpnAdapter();
        vpnAdapter.OpenGate();
        var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token), vpnAdapter: vpnAdapter);

        // the quick close holds at the adapter's release, which a failed look below still lets go on
        var disposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vpnAdapter.DisposeGate = disposeGate.Task;
        var quickClose = Task.Run(client.Dispose, TestCt);
        try {
            await vpnAdapter.DisposeWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var gracefulClose = client.DisposeAsync().AsTask();
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
            Assert.IsFalse(gracefulClose.IsCompleted, "The graceful close ended before the release.");

            disposeGate.TrySetResult();
            await quickClose.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            await gracefulClose.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            Assert.AreEqual(ClientState.Disposed, client.State);
        }
        finally {
            disposeGate.TrySetResult();
        }
    }

    // A filter whose close failed ended the client's close there: the adapter stayed up and the client never told of
    // its end, which the VPN service waits for, and the filters after it stayed open. The close goes on past a failed
    // step, which it logs, and so does the filters' own
    [TestMethod]
    public async Task A_close_goes_on_past_a_failed_step()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        using var vpnAdapter = new TestNullVpnAdapter();
        var isAdapterClosed = false;
        vpnAdapter.Disposed += (_, _) => isAdapterClosed = true;
        var domainFilter = new RecordingDomainFilter();
        var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token), vpnAdapter: vpnAdapter,
            netFilter: new NetFilter { IpFilter = new FailingCloseIpFilter(), DomainFilter = domainFilter });

        await client.DisposeAsync();
        Assert.IsTrue(isAdapterClosed, "The adapter stayed up.");
        Assert.IsTrue(domainFilter.IsDisposed, "The domain filter stayed open.");
        Assert.AreEqual(ClientState.Disposed, client.State);
    }

    // The filters' close threw what a part's close threw, once it had closed the others. A Dispose throws nothing: the
    // failed part is logged
    [TestMethod]
    public void The_filters_close_throws_nothing()
    {
        var domainFilter = new RecordingDomainFilter();
        var netFilter = new NetFilter { IpFilter = new FailingCloseIpFilter(), DomainFilter = domainFilter };

        netFilter.Dispose();
        Assert.IsTrue(domainFilter.IsDisposed, "The domain filter stayed open.");
    }

    // A connect paused before it wrote its record of the server wrote it after a newer client's of the same key, as its
    // close's teardown was still under way, and that close then deleted it as its own. A record takes no write once its
    // client's close has begun
    [TestMethod]
    public async Task A_record_takes_no_write_once_its_close_has_begun()
    {
        var folderPath = Path.Combine(TestHelper.WorkingPath, $"ConnectedServers_{Guid.NewGuid()}");
        var oldRecord = new ConnectedServerFile(folderPath, "key");
        var newRecord = new ConnectedServerFile(folderPath, "key");
        var newEndPoint = new VpnEndPoint(IPEndPoint.Parse("192.0.2.2:443"), "server.example", [1], null);

        oldRecord.Retire();
        newRecord.Write(newEndPoint);
        oldRecord.Write(new VpnEndPoint(IPEndPoint.Parse("192.0.2.1:443"), "server.example", [1], null));
        oldRecord.Close();

        var record = await newRecord.Read(TestCt);
        Assert.AreEqual(newEndPoint.TcpEndPoint, record?.TcpEndPoint);
        newRecord.Close();
    }

    // On Windows, whose paths are the same in any case, the close of a client that named its folder in another case than
    // a newer client deleted that client's record: the record's writer was looked up by the path as each wrote it
    [TestMethod]
    public async Task A_close_keeps_a_newer_clients_record_named_in_another_case()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows only: its paths are the same in any case.");

        // the two names one folder, as Windows' folders are but where they are set to tell case apart
        var folderPath = Path.Combine(TestHelper.WorkingPath, $"ConnectedServers_{Guid.NewGuid()}");
        Directory.CreateDirectory(folderPath.ToLowerInvariant());
        if (!Directory.Exists(folderPath.ToUpperInvariant()))
            Assert.Inconclusive("The test's folder tells case apart.");

        var oldRecord = new ConnectedServerFile(folderPath.ToUpperInvariant(), "key");
        var newRecord = new ConnectedServerFile(folderPath.ToLowerInvariant(), "key");
        var newEndPoint = new VpnEndPoint(IPEndPoint.Parse("192.0.2.2:443"), "server.example", [1], null);

        oldRecord.Write(new VpnEndPoint(IPEndPoint.Parse("192.0.2.1:443"), "server.example", [1], null));
        newRecord.Write(newEndPoint);
        oldRecord.Close();

        var record = await newRecord.Read(TestCt);
        Assert.AreEqual(newEndPoint.TcpEndPoint, record?.TcpEndPoint);
        newRecord.Close();
    }

    // A forced reconnect closes the old client in the background, and that close deleted the record of the server each
    // client was connected to, the new client's too, so after a crash of the VPN host the redial had no server to try
    // first. A close deletes its own client's record alone
    [TestMethod]
    public async Task A_close_keeps_the_server_record_of_a_newer_client()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var workingFolder = Path.Combine(TestHelper.WorkingPath, $"Client_{Guid.NewGuid()}");
        var recordPath = Path.Combine(workingFolder, "connected-servers",
            FileUtils.ReplaceInvalidFileNameChars(token.TokenId) + ".json");

        var oldClient = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), workingFolder: workingFolder);

        await using var newClient = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), workingFolder: workingFolder);

        Assert.IsTrue(File.Exists(recordPath), "The new client wrote no record.");

        await oldClient.DisposeAsync();
        Assert.IsTrue(File.Exists(recordPath), "The old client's close deleted the new client's record.");

        await newClient.DisposeAsync();
        Assert.IsFalse(File.Exists(recordPath), "The new client's close kept its own record.");
    }

    // a tracker that closes the client at the event of its connect
    private sealed class ClosingTracker : ITracker
    {
        public VpnHoodClient? Client { get; set; }
        public bool IsEnabled { get; set; } = true;

        public Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken)
        {
            return Task.WhenAll(trackEvents.Select(x => Track(x, cancellationToken)));
        }

        public Task Track(TrackEvent trackEvent, CancellationToken cancellationToken)
        {
            if (trackEvent.EventName == "vh_connect_attempt")
                Client?.Dispose();

            return Task.CompletedTask;
        }
    }

    // a domain filter that lets everything through, and notes its close
    private sealed class RecordingDomainFilter : IDomainFilter
    {
        public event EventHandler? Changed {
            add { }
            remove { }
        }

        public bool IsEmpty => true;
        public bool IsDisposed { get; private set; }

        public FilterAction Process(string? domainName)
        {
            return FilterAction.Default;
        }

        public Task Reconfigure(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    // a filter that lets everything through, and whose close fails
    private sealed class FailingCloseIpFilter : IIpFilter
    {
        public event EventHandler? Changed {
            add { }
            remove { }
        }

        public bool IsEmpty => true;

        public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
        {
            return FilterAction.Default;
        }

        public Task Reconfigure(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            throw new InvalidOperationException("The filter's close failed.");
        }
    }
}
