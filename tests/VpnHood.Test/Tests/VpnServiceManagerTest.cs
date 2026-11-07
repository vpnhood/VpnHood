using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Test.Device;

namespace VpnHood.Test.Tests;

[TestClass]
public class VpnServiceManagerTest : TestBase
{
    [TestMethod]
    public async Task Reading_the_status_file_does_not_fail_the_services_write()
    {
        using var device = TestHelper.CreateNullDevice();
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null);
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);

        // while the service disconnects, the manager learns its state from the file alone
        var statusJson = StatusJson(ClientState.Disconnecting);

        // the manager reads as fast as it can
        using var readCts = new CancellationTokenSource();
        var readTask = Task.Run(async () => {
            while (!readCts.IsCancellationRequested)
                await manager.RefreshState(TestCt);
        }, TestCt);

        // the service writes its state as VpnServiceContext does: a write a reader kept out throws, and the
        // service would lose that state
        try {
            for (var i = 0; i < 1000; i++)
                await File.WriteAllTextAsync(statusFilePath, statusJson, TestCt);
        }
        finally {
            await readCts.CancelAsync();
            await readTask;
        }

        // a read that caught the file half-written was passed over: the manager has what was written
        await manager.RefreshState(TestCt);
        Assert.AreEqual(ClientState.Disconnecting, manager.ConnectionInfo.ClientState);
    }

    [TestMethod]
    public async Task A_cancelled_start_that_ends_late_leaves_the_newer_start_its_state()
    {
        // each start waits at the VPN permission's ask until the test answers, and only then sees its cancel
        var asks = Channel.CreateUnbounded<TaskCompletionSource>();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            RequestVpnServiceHandler = async cancellationToken => {
                var answer = new TaskCompletionSource();
                await asks.Writer.WriteAsync(answer, TestCt);
                await answer.Task;
                cancellationToken.ThrowIfCancellationRequested();
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null);
        var serviceOptions = new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions() };

        // the first start is cancelled while it waits there
        using var firstCts = new CancellationTokenSource();
        var firstStart = manager.Start(uiContext: null, serviceOptions, firstCts.Token);
        var firstAnswer = await asks.Reader.ReadAsync(TestCt);
        await firstCts.CancelAsync();

        // the newer start's stop gives up on it; the newer one writes Initializing and waits there too
        using var secondCts = new CancellationTokenSource();
        var secondStart = manager.Start(uiContext: null, serviceOptions, secondCts.Token);
        var secondAnswer = await asks.Reader.ReadAsync(TestCt);

        // the first ends now and leaves the state to the newer one
        firstAnswer.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => firstStart);
        Assert.AreEqual(ClientState.Initializing, manager.ConnectionInfo.ClientState);
        Assert.IsNull(manager.ConnectionInfo.Error);

        // the newer start's end is its own to write
        await secondCts.CancelAsync();
        secondAnswer.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => secondStart);
        Assert.AreEqual(ClientState.Disposed, manager.ConnectionInfo.ClientState);
    }

    // The service keeps a reconfigure that comes before its session is up, and the session takes it once it
    // is; a start the service makes on its own (always-on, the tile) has no app to send it again.
    [TestMethod]
    public async Task A_reconfigure_sent_before_the_session_is_up_reaches_it()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);

        // the service's connect held where it makes its adapter: it has read the options, and has no client
        var adapterAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var device = new TestDevice(TestHelper, _ => {
            adapterAsked.TrySetResult();
            adapterRelease.Task.Wait(TimeSpan.FromSeconds(30));
            return new TestNullVpnAdapter();
        });
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null);
        var clientOptions = TestHelper.CreateClientOptions(token);
        var dropQuic = !clientOptions.DropQuic;
        try {
            var startTask = manager.Start(uiContext: null,
                new VpnServiceOptions { ClientOptions = clientOptions }, TestCt);
            await adapterAsked.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

            // no error with no session to take it. Dropping QUIC is the setting to watch: the test server
            // carries TCP only by forwarding, which drops it
            var reconfiguredOptions = TestHelper.CreateClientOptions(token);
            reconfiguredOptions.DropQuic = dropQuic;
            await manager.Reconfigure(new VpnServiceOptions { ClientOptions = reconfiguredOptions }, TestCt);
            adapterRelease.TrySetResult();
            await startTask;
        }
        finally {
            adapterRelease.TrySetResult();
        }

        await AssertEqualsWait(dropQuic, () => manager.ConnectionInfo.SessionStatus?.IsDropQuic,
            "the session's QUIC drop");
    }

    // A reconfigure that comes while a start brings its service up, before the service opens its channel: no
    // error, and the service starts with it, as it reads its options after it opens the channel. The channel file
    // is none yet, or an ended service's, as a Windows VPN host that its service's job ended leaves it.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_reconfigure_before_the_service_opens_its_channel_goes_into_its_start(bool endedServicesFile)
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);

        // the start held at the VPN permission's ask, before it starts the service
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            RequestVpnServiceHandler = async _ => {
                asked.TrySetResult();
                await release.Task;
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null);
        if (endedServicesFile) {
            // a port that answers nothing, as the ended service's listener is gone
            using var gone = new TcpListener(IPAddress.Loopback, 0);
            gone.Start();
            var goneEndPoint = (IPEndPoint)gone.LocalEndpoint;
            gone.Stop();
            await File.WriteAllTextAsync(TcpMessageTransport.GetApiFilePath(device.VpnServiceConfigFolder),
                JsonSerializer.Serialize(new TcpApiBootstrap { ApiEndPoint = goneEndPoint, ApiKey = new byte[32] }),
                TestCt);
        }

        var clientOptions = TestHelper.CreateClientOptions(token);
        var dropQuic = !clientOptions.DropQuic;
        var start = manager.Start(uiContext: null, new VpnServiceOptions { ClientOptions = clientOptions }, TestCt);
        try {
            await asked.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

            // Dropping QUIC is the setting to watch: the test server carries TCP only by forwarding, which drops it
            var reconfiguredOptions = TestHelper.CreateClientOptions(token);
            reconfiguredOptions.DropQuic = dropQuic;
            await manager.Reconfigure(new VpnServiceOptions { ClientOptions = reconfiguredOptions }, TestCt);
            release.TrySetResult();
            await start;
        }
        finally {
            release.TrySetResult();
        }

        await AssertEqualsWait(dropQuic, () => manager.ConnectionInfo.SessionStatus?.IsDropQuic,
            "the session's QUIC drop");
    }

    // Windows' VPN plug-in dial returns only once the channel is up, while the service finds and connects to the
    // server: the state it writes meanwhile shows before the start ends, but an older service's end does not.
    [TestMethod]
    public async Task A_start_still_running_shows_the_state_its_service_wrote()
    {
        // the start held at the VPN permission's ask, as Windows' dial holds the plug-in's
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            RequestVpnServiceHandler = async cancellationToken => {
                asked.TrySetResult();
                await release.Task;
                cancellationToken.ThrowIfCancellationRequested();
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null);
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        using var startCts = new CancellationTokenSource();
        var start = manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions() }, startCts.Token);
        try {
            await asked.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

            await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Disposed), TestCt);
            await manager.RefreshState(TestCt);
            Assert.AreEqual(ClientState.Initializing, manager.ConnectionInfo.ClientState, "an older service's end");

            await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Connecting), TestCt);
            await manager.RefreshState(TestCt);
            Assert.AreEqual(ClientState.Connecting, manager.ConnectionInfo.ClientState, "the service's own state");
        }
        finally {
            await startCts.CancelAsync();
            release.TrySetResult();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => start);
    }

    // the status file as the service writes it
    private static string StatusJson(ClientState clientState)
    {
        return JsonSerializer.Serialize(new ConnectionInfo {
            ClientState = clientState,
            CreatedTime = DateTime.UtcNow,
            ClientStateChangedTime = null,
            ClientStateProgress = null,
            ProxyConnectorStatus = null,
            SessionInfo = null,
            SessionStatus = null,
            Error = null
        });
    }
}
