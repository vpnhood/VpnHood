using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;

namespace VpnHood.Test.Tests;

[TestClass]
public class VpnServiceManagerTest : TestBase
{
    // a runner that never runs its jobs: no background refresh of a manager comes in the middle of a test, whose first
    // one would come at the shared runner's next tick, any time in the test's first 10 seconds
    private static readonly JobRunner IdleJobRunner = new(Timeout.InfiniteTimeSpan);

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

    // A start went on while the old session still tore down, as Disconnecting does not count as started, and the new
    // session could meet the old one's resources still held. It waits for the old one's end
    [TestMethod]
    public async Task A_start_waits_for_the_old_sessions_teardown()
    {
        // the start reaches the VPN permission's ask only after its stop, and the test ends it there
        var tornDown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startCts = new CancellationTokenSource();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            RequestVpnServiceHandler = async cancellationToken => {
                asked.TrySetResult(tornDown.Task.IsCompleted);
                await startCts.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null);
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Disconnecting), TestCt);
        await manager.RefreshState(TestCt);

        var start = manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions() }, startCts.Token);

        await AssertEqualsWait(true, () => manager.IsStopping || asked.Task.IsCompleted, "the start's stop");
        Assert.IsFalse(asked.Task.IsCompleted, "The start went on while the old session tore down.");

        tornDown.TrySetResult();
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Disposed), TestCt);
        Assert.IsTrue(await asked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCt));
        await Assert.ThrowsAsync<OperationCanceledException>(() => start);
    }

    // The wait for the service's end had only what the disconnect request left of its time: where the service did not
    // answer in time, the stop gave up at once, though the service ended a moment later. The wait has 5 seconds of its own
    [TestMethod]
    public async Task A_stop_whose_request_used_up_its_time_still_waits_for_the_end()
    {
        using var device = TestHelper.CreateNullDevice();
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);

        // the service takes the request and answers nothing, while it tears down
        using var service = new TcpListener(IPAddress.Loopback, 0);
        service.Start();
        await File.WriteAllTextAsync(TcpMessageTransport.GetApiFilePath(device.VpnServiceConfigFolder),
            JsonSerializer.Serialize(new TcpApiBootstrap {
                ApiEndPoint = (IPEndPoint)service.LocalEndpoint,
                ApiKey = new byte[32]
            }), TestCt);

        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Disconnecting), TestCt);
        await manager.RefreshState(TestCt);

        // its end comes a second after the request's time
        var requestTimeout = TimeSpan.FromMilliseconds(500);
        var stop = manager.TryStop(requestTimeout);
        await Task.Delay(requestTimeout + TimeSpan.FromSeconds(1), TestCt);
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Disposed), TestCt);

        Assert.IsTrue(await stop.WaitAsync(TimeSpan.FromSeconds(10), TestCt),
            "The stop gave up with the request's time.");
    }

    // The service's word of its end for a disconnect carried the disconnect's reason alone, so where it came before the
    // client's own, which tells of the session's error, the app saw the session end with none. It carries the session's
    [TestMethod]
    public async Task A_disconnect_keeps_the_error_the_session_ended_with()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);

        // The client's own word of its end is held until the test has read the disconnect's, which comes first; no
        // background refresh asks the service meanwhile, whose answer would write the client's word another way
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            ClientCreated = client => client.StateChanged += (_, _) => {
                if (client.State is ClientState.Disconnecting or ClientState.Disposed)
                    release.Task.Wait();
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        try {
            await manager.Start(uiContext: null,
                new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

            var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
            var client = service.LastClient ?? throw new InvalidOperationException("The service has no client.");

            // the server closes the session, and the client ends with that error
            await server.SessionManager.CloseSession(client.SessionId, TestCt);
            await client.WaitForState(ClientState.Disposed, TestCt, useUpdateStatus: true);
            Assert.IsInstanceOfType<SessionException>(client.LastException);

            await service.Disconnect();

            // read while the client's own word is still held, so the end read is the disconnect's
            var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
            var connectionInfo = ReadStatus(statusFilePath);
            Assert.AreEqual(ClientState.Disposed, connectionInfo?.ClientState);
            Assert.IsTrue(connectionInfo?.Error?.Is<SessionException>(), "The session's error was lost.");
            Assert.IsNotNull(connectionInfo?.SessionInfo, "The session's info was lost.");
        }
        finally {
            release.TrySetResult();
        }
    }

    // The message client sends no request again after the service may have taken it, so a status request whose answer
    // was cut on the kept connection showed the service gone while it ran. The manager asks a status request once more.
    // The channel is one that sends nothing again of its own, so the second request is the manager's
    [TestMethod]
    public async Task A_status_request_whose_answer_was_cut_is_asked_once_more()
    {
        var messageClient = new CutAnswerMessageClient(cutRequestNumber: 1);
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            MessageClientFactory = () => messageClient
        };

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Connected), TestCt);

        await manager.RefreshState(TestCt);
        Assert.AreEqual(2, messageClient.RequestCount);
        Assert.AreEqual(ClientState.Connected, manager.ConnectionInfo.ClientState);
    }

    // A status request had no time limit: an answer that never came held the poll's locks, so the state shown froze and
    // a disconnect could not be sent. One not answered in time, at each of its two tries, reads as the service gone
    [TestMethod]
    public async Task A_status_request_the_service_never_answers_ends_in_time()
    {
        using var device = TestHelper.CreateNullDevice();
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner) {
            StatusAnswerTimeout = TimeSpan.FromSeconds(2)
        };

        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Connected), TestCt);

        // the service takes the requests and never answers, as a hung one
        var requestCount = 0;
        using var listener = new TcpMessageListener(device.VpnServiceConfigFolder);
        var listenerTask = listener.Start(async (_, cancellationToken) => {
            Interlocked.Increment(ref requestCount);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Memory<byte>.Empty;
        }, TestCt);

        await manager.RefreshState(TestCt).WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.AreEqual(2, requestCount, "The service did not take the request and its one more.");
        var connectionInfo = manager.ConnectionInfo;
        Assert.AreEqual(ClientState.Disposed, connectionInfo.ClientState);
        Assert.IsTrue(connectionInfo.Error?.Is<VpnServiceUnreachableException>(), "The end is not the service's.");
        StringAssert.Contains(connectionInfo.Error?.Message, "did not answer");

        listener.Dispose();
        await listenerTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
    }

    // A status request whose answer did not come in time, as one Android lost or one that came late for an app frozen
    // meanwhile, showed the running service as gone. It is asked once more, which the service answers
    [TestMethod]
    public async Task A_status_request_not_answered_in_time_is_asked_once_more()
    {
        using var device = TestHelper.CreateNullDevice();
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner) {
            StatusAnswerTimeout = TimeSpan.FromSeconds(2)
        };

        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Connected), TestCt);

        // the service's first answer is lost, the next one comes
        var requestCount = 0;
        using var listener = new TcpMessageListener(device.VpnServiceConfigFolder);
        var listenerTask = listener.Start(async (_, cancellationToken) => {
            if (Interlocked.Increment(ref requestCount) == 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return CreateAnswer(ClientState.Connected);
        }, TestCt);

        await manager.RefreshState(TestCt).WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.AreEqual(2, requestCount);
        Assert.AreEqual(ClientState.Connected, manager.ConnectionInfo.ClientState);

        listener.Dispose();
        await listenerTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
    }

    // An unblock that took the block down and then failed, as on Linux where the block's boot copy could not be
    // deleted, left the switch taken for on, so turning it on again was taken for done, and nothing blocked. After a
    // failed change, the next one is made whatever the switch was
    [TestMethod]
    public async Task A_switch_turned_on_after_a_failed_unblock_goes_up()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) { KillSwitch = killSwitch };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null, CreateServiceOptions(token, useKillSwitch: true), TestCt);
        Assert.IsTrue(killSwitch.IsUp);

        // turned off: the block goes down, and the unblock fails after
        killSwitch.UnblockException = new IOException("The block's boot copy could not be deleted.");
        await Assert.ThrowsAsync<Exception>(() =>
            manager.Reconfigure(CreateServiceOptions(token, useKillSwitch: false), TestCt));

        Assert.IsFalse(killSwitch.IsUp);

        killSwitch.UnblockException = null;
        await manager.Reconfigure(CreateServiceOptions(token, useKillSwitch: true), TestCt);
        Assert.IsTrue(killSwitch.IsUp, "The switch turned on again is off.");
        await manager.TryStop();
    }

    // A switch turned on while a reconnect started went onto the last session's block, which the reconnect then set up
    // by the options it had read, without the switch. The reconnect takes the switch it was last asked for. The
    // reconnect is held at its first notification, once it has read its options and before its block, and the request
    // comes without the options the app would write first, so only the request can bring the switch
    [TestMethod]
    public async Task A_switch_turned_on_while_a_reconnect_starts_goes_up()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        var isUpAtAdapterStart = false;
        using var device = new TestDevice(TestHelper, _ => {
            var vpnAdapter = new TestNullVpnAdapter();
            vpnAdapter.Started += (_, _) => isUpAtAdapterStart = killSwitch.IsUp;
            return vpnAdapter;
        }) { KillSwitch = killSwitch };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null, CreateServiceOptions(token, useKillSwitch: false), TestCt);
        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var oldClient = service.LastClient;
        Assert.IsFalse(killSwitch.IsUp);

        var notificationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.NotificationGate = notificationGate.Task;
        var reconnect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            await service.NotificationWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            await service.Reconfigure(CreateReconfigureParams(useKillSwitch: true));
        }
        finally {
            notificationGate.TrySetResult();
        }

        Assert.IsTrue(await reconnect.WaitAsync(TimeSpan.FromSeconds(30), TestCt));
        var newClient = service.LastClient;
        Assert.AreNotSame(oldClient, newClient);
        Assert.AreEqual(ClientState.Connected, newClient?.State, "The reconnect did not connect.");
        Assert.IsTrue(isUpAtAdapterStart, "The block was not up as the reconnect's adapter started.");
        await manager.TryStop();
    }

    // A reconfigure whose kill switch change failed kept nothing, so the proxies' reset it carried, which the app sends
    // once, was lost. It is kept before the change, for the next one to carry
    [TestMethod]
    public async Task A_proxies_reset_outlives_a_failed_kill_switch_change()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) { KillSwitch = killSwitch };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null, CreateServiceOptions(token, useKillSwitch: false), TestCt);
        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");

        // the switch turned on, which the OS refuses, with a reset of the proxies
        killSwitch.BlockException = new UnauthorizedAccessException("The firewall refused the block.");
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.UseKillSwitch = true;
        var serviceOptions = new VpnServiceOptions {
            ClientOptions = clientOptions,
            ProxyOptions = new ProxyOptions { ResetStates = true }
        };
        await Assert.ThrowsAsync<Exception>(() => manager.Reconfigure(serviceOptions, TestCt));

        Assert.IsTrue(service.KeptReconfigure?.ProxyOptions.ResetStates, "The reset was lost.");
        await manager.TryStop();
    }

    // A disconnect that waited its turn at the kill switch read the session and the client after it, which a forced
    // connect begun meanwhile had replaced: the disconnect ended the new session in the state the app reads, which then
    // took no word of the new connect, or closed its client. A disconnect ends its own session alone
    [TestMethod]
    public async Task A_disconnect_ends_its_own_session_not_a_connect_begun_since()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) { KillSwitch = killSwitch };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null, CreateServiceOptions(token, useKillSwitch: true), TestCt);
        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var oldClient = service.LastClient ?? throw new InvalidOperationException("The service has no client.");

        // the switch turned off, and on again with its block held as it goes up: the switch's turn is taken meanwhile
        await manager.Reconfigure(CreateServiceOptions(token, useKillSwitch: false), TestCt);
        var blockGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        killSwitch.BlockGate = blockGate.Task;
        var reconfigure = manager.Reconfigure(CreateServiceOptions(token, useKillSwitch: true), TestCt);

        // a disconnect, which waits for the switch's turn, and a forced connect, which begins its session meanwhile
        try {
            await killSwitch.BlockWaiting.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
            var oldSessionId = service.SessionId;
            var disconnect = service.Disconnect();
            var connect = service.Connect(forceReconnect: true);
            await AssertEqualsWait(true, () => service.SessionId > oldSessionId, "the new connect's session");
            blockGate.TrySetResult();

            await disconnect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            await reconfigure.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            Assert.IsTrue(await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt));
        }
        finally {
            blockGate.TrySetResult();
        }

        var newClient = service.LastClient;
        Assert.AreNotSame(oldClient, newClient);
        Assert.AreEqual(ClientState.Connected, newClient?.State, "The disconnect closed the new connect's client.");

        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await AssertEqualsWait(ClientState.Connected, () => ReadStatusState(statusFilePath),
            "the new connect's state, as the app reads it");

        await manager.TryStop();
    }

    // A connect a newer one took over, whose factory made its client all the same, as one that takes no notice of a
    // cancel, made that client the service's: the newer connect's client went unfollowed, the old client's end was
    // written as the newer session's, and it stopped the service. A client made once a newer connect has begun goes
    [TestMethod]
    public async Task A_client_made_after_a_newer_connect_began_goes()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var clientGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientCount = 0;

        // the second adapter, the held connect's, which goes with its client
        var adapterCount = 0;
        var isHeldAdapterClosed = false;
        using var device = new TestDevice(TestHelper, _ => {
            var vpnAdapter = new TestNullVpnAdapter();
            if (Interlocked.Increment(ref adapterCount) == 2)
                vpnAdapter.Disposed += (_, _) => isHeldAdapterClosed = true;

            return vpnAdapter;
        }) {
            // the second client, the first forced connect's, is held as it is made
            ClientCreated = _ => {
                if (Interlocked.Increment(ref clientCount) != 2)
                    return;

                clientWaiting.TrySetResult();
                clientGate.Task.Wait(CancellationToken.None);
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");

        var oldConnect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            await clientWaiting.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            Assert.IsTrue(await service.Connect(forceReconnect: true).WaitAsync(TimeSpan.FromSeconds(30), TestCt));
        }
        finally {
            clientGate.TrySetResult();
        }

        // the newer connect's client is the last one made, the held one before it
        await oldConnect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        var newClient = service.LastClient;
        Assert.AreSame(newClient, service.Client, "The old connect's client took the newer one's place.");
        Assert.AreEqual(ClientState.Connected, newClient?.State);
        Assert.IsTrue(isHeldAdapterClosed, "The held client's adapter is still open.");

        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await AssertEqualsWait(ClientState.Connected, () => ReadStatusState(statusFilePath),
            "the newer connect's state, as the app reads it");

        await manager.TryStop();
    }

    // A connect that took over one held as its block went up ended it before its own session began: the older one's
    // end, which saw no newer session, released the tunnel and stopped the service, and the newer connect found the
    // service gone. The newer session begins before the older connect ends
    [TestMethod]
    public async Task A_connect_that_takes_over_one_at_its_block_connects()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) { KillSwitch = killSwitch };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null, CreateServiceOptions(token, useKillSwitch: true), TestCt);
        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        await AssertEqualsWait(2, () => killSwitch.BlockCount, "the first connect's block and its re-point");

        // a forced connect held as its block goes up, and a newer forced connect meanwhile, whose block goes up at once;
        // the session the service is in as the older connect is ended
        var blockGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        killSwitch.BlockGate = blockGate.Task;
        var sessionAtCancel = 0;
        killSwitch.BlockCanceled = () => sessionAtCancel = service.SessionId;
        var oldConnect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            await killSwitch.BlockWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var oldSessionId = service.SessionId;
            killSwitch.BlockGate = null;
            Assert.IsTrue(await service.Connect(forceReconnect: true).WaitAsync(TimeSpan.FromSeconds(30), TestCt),
                "The newer connect was refused.");

            Assert.IsTrue(sessionAtCancel > oldSessionId, "The older connect was ended before the newer session began.");
        }
        finally {
            blockGate.TrySetResult();
        }

        await oldConnect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        Assert.IsFalse(service.IsDisposed, "The older connect's end stopped the service.");
        Assert.AreEqual(ClientState.Connected, service.LastClient?.State);
        await manager.TryStop();
    }

    // A revoke marked the stop, took the adapter down, then stopped the session the service was in by then: a connect
    // begun as the adapter went down, the person's own after the revoke, was ended by it. A revoke ends the session it
    // came for alone
    [TestMethod]
    public async Task A_revoke_ends_its_own_session_not_a_connect_begun_since()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var oldAdapter = new GatedVpnAdapter();
        oldAdapter.OpenGate();
        var adapterCount = 0;
        using var device = new TestDevice(TestHelper,
            _ => Interlocked.Increment(ref adapterCount) == 1 ? oldAdapter : new TestNullVpnAdapter());

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var oldClient = service.LastClient;

        // the revoke holds as its adapter goes down, while a forced connect begins its session
        var disposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldAdapter.DisposeGate = disposeGate.Task;
        var revoke = Task.Run(service.Revoke, TestCt);
        try {
            await oldAdapter.DisposeWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var sessionId = service.SessionId;
            var connect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
            await AssertEqualsWait(true, () => service.SessionId > sessionId, "the new connect's session");
            disposeGate.TrySetResult();
            Assert.IsTrue(await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt));
            await revoke.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        }
        finally {
            disposeGate.TrySetResult();
        }

        var newClient = service.LastClient;
        Assert.AreNotSame(oldClient, newClient);
        Assert.AreEqual(ClientState.Connected, newClient?.State, "The revoke ended the new connect.");
        await manager.TryStop();
    }

    // A connect that is not forced, as Android's always-on start, took over one still starting, as the app's, which had
    // no client to show it under way yet: the first was canceled and the second went on, a system's start the person's
    // account may not be allowed. A connect still starting is under way too
    [TestMethod]
    public async Task A_connect_that_is_not_forced_leaves_one_still_starting()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var clientGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientCount = 0;
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            // the first client, the start's, is held as it is made
            ClientCreated = _ => {
                if (Interlocked.Increment(ref clientCount) != 1)
                    return;

                clientWaiting.TrySetResult();
                clientGate.Task.Wait(CancellationToken.None);
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        var start = manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        try {
            await clientWaiting.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
            Assert.IsFalse(await service.Connect(forceReconnect: false).WaitAsync(TimeSpan.FromSeconds(30), TestCt),
                "The connect that is not forced took over.");
        }
        finally {
            clientGate.TrySetResult();
        }

        await start.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        var startedService = device.VpnService ?? throw new InvalidOperationException("The service has stopped.");
        Assert.AreEqual(ClientState.Connected, startedService.Client?.State, "The first connect did not connect.");
        Assert.AreEqual(1, clientCount);
        await manager.TryStop();
    }

    // A disconnect while a connect was still making its client wrote the session's end at once, while that connect still
    // held the tunnel and its adapter: the app took the service for stopped and could start the next connect beside
    // them. The end is written once the connect has let go
    [TestMethod]
    public async Task A_disconnect_ends_once_a_connect_still_starting_lets_go()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var clientGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientCount = 0;
        var adapterCount = 0;
        var isHeldAdapterClosed = false;
        using var device = new TestDevice(TestHelper, _ => {
            var vpnAdapter = new TestNullVpnAdapter();
            if (Interlocked.Increment(ref adapterCount) == 2)
                vpnAdapter.Disposed += (_, _) => isHeldAdapterClosed = true;

            return vpnAdapter;
        }) {
            // the second client, a forced connect's, is held as it is made, its adapter made before it
            ClientCreated = _ => {
                if (Interlocked.Increment(ref clientCount) != 2)
                    return;

                clientWaiting.TrySetResult();
                clientGate.Task.Wait(CancellationToken.None);
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");

        var connect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            await clientWaiting.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
            var disconnect = service.Disconnect();
            await Task.WhenAny(disconnect, Task.Delay(TimeSpan.FromSeconds(2), TestCt));
            Assert.IsFalse(disconnect.IsCompleted, "The disconnect ended while the connect still held its adapter.");
            Assert.AreNotEqual(ClientState.Disposed, (await ReadStatusWait(statusFilePath)).ClientState,
                "The session's end was written while the connect still held its adapter.");

            clientGate.TrySetResult();
            await disconnect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            Assert.IsTrue(isHeldAdapterClosed, "The session's end came before its adapter's close.");
        }
        finally {
            clientGate.TrySetResult();
        }

        await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        Assert.AreEqual(ClientState.Disposed,
            ReadStatusState(Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName)));
    }

    // A client made as a revoke took the adapter down, before the revoke's disconnect had canceled its connect, was
    // made the service's: the connect went on, its session up for a moment on the tunnel another app had asked for. A
    // client made once a stop is asked for goes
    [TestMethod]
    public async Task A_client_made_as_a_revoke_takes_the_adapter_down_goes()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var clientGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientCount = 0;
        var heldAdapter = new GatedVpnAdapter();
        heldAdapter.OpenGate();
        var adapterCount = 0;
        using var device = new TestDevice(TestHelper,
            _ => Interlocked.Increment(ref adapterCount) == 2 ? heldAdapter : new TestNullVpnAdapter()) {
            // the second client, a forced connect's, is held as it is made, its adapter made before it
            ClientCreated = _ => {
                if (Interlocked.Increment(ref clientCount) != 2)
                    return;

                clientWaiting.TrySetResult();
                clientGate.Task.Wait(CancellationToken.None);
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");

        // the connect held as its client is made; the revoke held as it takes that connect's adapter down
        var disposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        heldAdapter.DisposeGate = disposeGate.Task;
        var connect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            await clientWaiting.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var revoke = Task.Run(service.Revoke, TestCt);
            await heldAdapter.DisposeWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

            // the client made: made the service's, or closed, the revoke's close of the adapter still held
            clientGate.TrySetResult();
            await AssertEqualsWait(true, () => service.Client != null || heldAdapter.DisposeCallCount >= 2,
                "the made client's way");

            Assert.IsNull(service.Client, "The client made after the revoke was made the service's.");

            // the connect's end, while the revoke is held: it tells of the revoke, not of the cancel the revoke brought
            await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var connectionInfo =
                ReadStatus(Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName));

            Assert.AreEqual(ClientState.Disposed, connectionInfo?.ClientState);
            Assert.IsTrue(connectionInfo?.Error?.Is<VpnServiceRevokedException>(), "The end does not tell of the revoke.");

            disposeGate.TrySetResult();
            await revoke.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        }
        finally {
            clientGate.TrySetResult();
            disposeGate.TrySetResult();
        }
    }

    // A disconnect waited for the last connect alone, so where that one had taken over an older one still making its
    // client, the session's end came while the older one still held its adapter, and the app could start the next
    // connect beside it. It waits for every connect begun before it
    [TestMethod]
    public async Task A_disconnect_ends_once_a_connect_taken_over_lets_go()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var clientGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientCount = 0;
        var adapterCount = 0;
        var isHeldAdapterClosed = false;
        using var device = new TestDevice(TestHelper, _ => {
            var vpnAdapter = new TestNullVpnAdapter();
            if (Interlocked.Increment(ref adapterCount) == 2)
                vpnAdapter.Disposed += (_, _) => isHeldAdapterClosed = true;

            return vpnAdapter;
        }) {
            // the second client, the first forced connect's, is held as it is made
            ClientCreated = _ => {
                if (Interlocked.Increment(ref clientCount) != 2)
                    return;

                clientWaiting.TrySetResult();
                clientGate.Task.Wait(CancellationToken.None);
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");

        // the held connect is taken over by a newer forced connect, which connects; that one is then disconnected
        var heldConnect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            await clientWaiting.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            Assert.IsTrue(await service.Connect(forceReconnect: true).WaitAsync(TimeSpan.FromSeconds(30), TestCt));

            var disconnect = service.Disconnect();
            await Task.WhenAny(disconnect, Task.Delay(TimeSpan.FromSeconds(2), TestCt));
            Assert.IsFalse(disconnect.IsCompleted, "The disconnect ended while a connect taken over held its adapter.");

            clientGate.TrySetResult();
            await disconnect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            Assert.IsTrue(isHeldAdapterClosed, "The session's end came before the taken-over connect's adapter closed.");
        }
        finally {
            clientGate.TrySetResult();
        }

        await heldConnect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
    }

    // A connect after one that failed with the tunnel, whose hold was still on its way out, asked that hold for the
    // tunnel as if another app held it, and the hold's answer revoked the new connect. A take waits for this host's last
    // hold to be let go
    [TestMethod]
    public async Task A_connect_after_a_failed_one_takes_the_tunnel_once_its_hold_is_let_go()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch {
            BlockException = new UnauthorizedAccessException("The firewall refused the block.")
        };

        var tunnelLock = new TestVpnTunnelLock();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            KillSwitch = killSwitch,
            TunnelLock = tunnelLock
        };

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);

        // the first connect takes the tunnel, fails at its block, and its hold's close is held
        var holdDisposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tunnelLock.HoldDisposeGate = holdDisposeGate.Task;
        var start = manager.Start(uiContext: null, CreateServiceOptions(token, useKillSwitch: true), TestCt);
        try {
            await tunnelLock.HoldDisposeWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
            killSwitch.BlockException = null;
            var connect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);

            // the new connect's take: it waits for the hold's end, or asks the hold, as another app's
            await Task.WhenAny(tunnelLock.Asked, service.TunnelTakeWaiting).WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            holdDisposeGate.TrySetResult();
            Assert.IsTrue(await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt));
            Assert.AreEqual(ClientState.Connected, service.Client?.State, "The new connect did not connect.");
            Assert.IsFalse(tunnelLock.Asked.IsCompleted, "The new connect asked the failed one's hold.");
        }
        finally {
            holdDisposeGate.TrySetResult();
            tunnelLock.HoldDisposeGate = null;
        }

        // the start's wait sees the new connect's state
        await start.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        await manager.TryStop();
    }

    // A connect that took over one whose take of the tunnel was still under way, Windows' mutex thread holding the
    // tunnel before the host had the hold, found no hold and asked for the tunnel, and the older take's hold heard the ask
    // as another app's and revoked the new connect. A take waits for a take under way
    [TestMethod]
    public async Task A_connect_that_takes_over_one_taking_the_tunnel_waits_for_its_take()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var takeAcquiredGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tunnelLock = new TestVpnTunnelLock { TakeAcquiredGate = takeAcquiredGate.Task };
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) { TunnelLock = tunnelLock };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);

        // the start's take holds the tunnel and is held before it returns its hold; a forced connect meanwhile
        var start = manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        try {
            await tunnelLock.TakeAcquiredWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            tunnelLock.TakeAcquiredGate = null;
            var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
            var connect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);

            // the new connect's take: it waits for the older take, or asks its hold, as another app's
            await Task.WhenAny(tunnelLock.Asked, service.TunnelTakeWaiting).WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            takeAcquiredGate.TrySetResult();
            Assert.IsTrue(await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt));
            Assert.AreEqual(ClientState.Connected, service.Client?.State, "The new connect did not connect.");
            Assert.IsFalse(tunnelLock.Asked.IsCompleted, "The new connect asked the older take's hold.");
        }
        finally {
            takeAcquiredGate.TrySetResult();
        }

        await start.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        await manager.TryStop();
    }

    // A stop of a connect that had taken over one still making its client wrote the session's end by the client's own
    // close, which waited for nothing, while the older connect still held its adapter: the app took the service for
    // stopped. The client's own end waits for every connect begun before it too, and no answer to the app tells it first
    [TestMethod]
    public async Task A_stop_ends_once_a_connect_taken_over_lets_go()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var clientGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientCount = 0;
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            // the second client, the first forced connect's, is held as it is made
            ClientCreated = _ => {
                if (Interlocked.Increment(ref clientCount) != 2)
                    return;

                clientWaiting.TrySetResult();
                clientGate.Task.Wait(CancellationToken.None);
            }
        };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);

        // the held connect is taken over by a newer forced connect, which connects; the app then stops the service, whose
        // client closes while the connect taken over still holds its adapter
        var heldConnect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            await clientWaiting.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            Assert.IsTrue(await service.Connect(forceReconnect: true).WaitAsync(TimeSpan.FromSeconds(30), TestCt));
            var activeClient = service.Client ?? throw new InvalidOperationException("The connect has no client.");

            var stop = manager.TryStop();
            await AssertEqualsWait(ClientState.Disposed, () => activeClient.State, "the active client's close");
            Assert.IsFalse(stop.IsCompleted, "The stop ended while a connect taken over held its adapter.");
            await AssertEndPending(device.VpnServiceConfigFolder, "a connect taken over held its adapter");

            clientGate.TrySetResult();
            Assert.IsTrue(await stop.WaitAsync(TimeSpan.FromSeconds(30), TestCt), "The stop did not see the end.");
            Assert.AreEqual(ClientState.Disposed, (await ReadStatusWait(statusFilePath)).ClientState,
                "The session's end is not in the state.");
        }
        finally {
            clientGate.TrySetResult();
        }

        await heldConnect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
    }

    // A stop of a connect that had replaced a connected client wrote the session's end while that client's close, which
    // a forced connect runs beside it, still held its filters and adapter: the app took the service for stopped and
    // could open them again. A stop waits for a replaced client's close, and no answer to the app tells the end first
    [TestMethod]
    public async Task A_stop_ends_once_a_replaced_client_has_closed()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var oldAdapter = new GatedVpnAdapter();
        oldAdapter.OpenGate();
        var adapterCount = 0;
        using var device = new TestDevice(TestHelper,
            _ => Interlocked.Increment(ref adapterCount) == 1 ? oldAdapter : new TestNullVpnAdapter());

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);

        // a forced connect replaces the client, whose close is held at its adapter's; the app then stops the service,
        // whose new client closes while the replaced one still does
        var disposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldAdapter.DisposeGate = disposeGate.Task;
        try {
            Assert.IsTrue(await service.Connect(forceReconnect: true).WaitAsync(TimeSpan.FromSeconds(30), TestCt));
            await oldAdapter.DisposeWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var activeClient = service.Client ?? throw new InvalidOperationException("The connect has no client.");

            var stop = manager.TryStop();
            await AssertEqualsWait(ClientState.Disposed, () => activeClient.State, "the active client's close");
            Assert.IsFalse(stop.IsCompleted, "The stop ended while a replaced client still closed.");
            await AssertEndPending(device.VpnServiceConfigFolder, "a replaced client still closed");

            disposeGate.TrySetResult();
            Assert.IsTrue(await stop.WaitAsync(TimeSpan.FromSeconds(30), TestCt), "The stop did not see the end.");
            Assert.AreEqual(ClientState.Disposed, (await ReadStatusWait(statusFilePath)).ClientState,
                "The session's end is not in the state.");
        }
        finally {
            disposeGate.TrySetResult();
        }
    }

    // A connect that replaced a connected client and then failed before it had a client of its own wrote its end and
    // stopped the service at once, while the replaced client's close still held its filters and adapter: the app took the
    // service for stopped and could open them again. The failed connect's end waits for that close
    [TestMethod]
    public async Task A_failed_connect_ends_once_the_replaced_client_has_closed()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var oldAdapter = new GatedVpnAdapter();
        oldAdapter.OpenGate();
        var adapterFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterCount = 0;
        using var device = new TestDevice(TestHelper, _ => {
            if (Interlocked.Increment(ref adapterCount) == 1)
                return oldAdapter;

            adapterFailed.TrySetResult();
            throw new InvalidOperationException("The adapter could not be made.");
        });
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);

        // a forced connect replaces the client, whose close is held at its adapter's, and fails at its own adapter
        var disposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldAdapter.DisposeGate = disposeGate.Task;
        var connect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            // the failed connect's end: its wait for the replaced client's close, or the service's stop
            await oldAdapter.DisposeWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            await adapterFailed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            await AssertEqualsWait(true, () => service.PredecessorsWaiting.IsCompleted || service.IsDisposed,
                "the failed connect's end", timeout: 30_000);

            Assert.IsFalse(service.IsDisposed, "The service stopped while a replaced client still closed.");
            Assert.AreNotEqual(ClientState.Disposed, (await ReadStatusWait(statusFilePath)).ClientState,
                "The failed connect's end was written while a replaced client still closed.");

            disposeGate.TrySetResult();
            await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        }
        finally {
            disposeGate.TrySetResult();
        }

        var connectionInfo = await ReadStatusWait(statusFilePath);
        Assert.AreEqual(ClientState.Disposed, connectionInfo.ClientState, "The failed connect's end is not in the state.");
        Assert.IsTrue(connectionInfo.Error?.Is<InvalidOperationException>() is true &&
                      connectionInfo.Error.Message.Contains("could not be made"),
            "The failed connect's end does not tell the adapter's failure.");

        await AssertEqualsWait(true, () => service.IsDisposed, "the service's stop");
    }

    // A disconnect while the session's adapter started, its client made, ended the session with the cancel it brought as
    // its error, which the app showed for the person's own disconnect: the cancel reached the connect before the client
    // knew of its close. A disconnect with no reason ends the session with no error
    [TestMethod]
    public async Task A_disconnect_as_the_connect_starts_its_adapter_leaves_no_error()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var heldAdapter = new GatedVpnAdapter();
        var adapterCount = 0;
        using var device = new TestDevice(TestHelper,
            _ => Interlocked.Increment(ref adapterCount) == 2 ? heldAdapter : new TestNullVpnAdapter());

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");

        // a forced connect held as its adapter starts, its client made, and a disconnect meanwhile
        var connect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            await heldAdapter.StartWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var disconnect = service.Disconnect();
            heldAdapter.OpenGate();
            await disconnect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        }
        finally {
            heldAdapter.OpenGate();
        }

        await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        var connectionInfo = ReadStatus(Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName));
        Assert.AreEqual(ClientState.Disposed, connectionInfo?.ClientState);
        Assert.IsNull(connectionInfo?.Error, "The person's own disconnect ended the session with an error.");
    }

    // A disconnect began the client's close before it took its turn at the kill switch, so the client's own end reached
    // the app while a change of the block was still under way in the service, a re-point at an adapter's restart say: the
    // app lifted the block at that end, and the change landed after, the block up with no VPN. The session's changes
    // end first
    [TestMethod]
    public Task A_disconnect_ends_once_the_sessions_kill_switch_change_has_landed()
    {
        return AssertStopEndsOnceKillSwitchChangeHasLanded(service => service.Disconnect());
    }

    // Another app's take of the tunnel took the adapter down, whose close ended the client, and the client's own end
    // reached the app while a change of the block was still under way in the service, its Disconnecting first, which the
    // app takes for the end: the app lifted the block at it, and the change landed after, the block up under the other
    // app's VPN. The session's changes end before the app hears the session is on its way down
    [TestMethod]
    public Task A_revoke_ends_once_the_sessions_kill_switch_change_has_landed()
    {
        return AssertStopEndsOnceKillSwitchChangeHasLanded(service => service.Revoke());
    }

    // a re-point held as its block goes up, at the adapter's restart, and the stop given meanwhile: the app hears of no
    // way down, Disconnecting nor the end, which it takes for the end alike, until the held change has landed
    private async Task AssertStopEndsOnceKillSwitchChangeHasLanded(Func<TestVpnService, Task> stopService)
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        var vpnAdapter = new GatedVpnAdapter();
        vpnAdapter.OpenGate();
        using var device = new TestDevice(TestHelper, _ => vpnAdapter) { KillSwitch = killSwitch };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null, CreateServiceOptions(token, useKillSwitch: true), TestCt);
        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var startOptions = vpnAdapter.StartOptions ?? throw new InvalidOperationException("The adapter has not started.");
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await AssertEqualsWait(2, () => killSwitch.BlockCount, "the connect's block and its re-point");

        // a re-point held as its block goes up, at the adapter's restart, and the stop meanwhile; the state is read as
        // the held change lands
        var blockGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ClientState? stateAtLanding = null;
        killSwitch.BlockGate = blockGate.Task;
        killSwitch.BlockLanding = () => stateAtLanding = ReadStatusNow(statusFilePath).ClientState;
        try {
            vpnAdapter.RaiseStarted(startOptions);
            await killSwitch.BlockWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var stop = stopService(service);
            await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(2), TestCt));
            Assert.AreEqual(ClientState.Connected, (await ReadStatusWait(statusFilePath)).ClientState,
                "The session's way down reached the app while its change of the block was under way.");

            // an answer to the app meanwhile tells the last word published, with no wait for the change
            Assert.AreEqual(ClientState.Connected,
                (await AskStatus(device.VpnServiceConfigFolder, TimeSpan.FromSeconds(5))).ClientState,
                "The service's answer told the session's way down while its change of the block was under way.");

            // the end, once the held change has landed, as the app still read it connected then
            blockGate.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            await AssertEqualsWait(ClientState.Disposed, () => ReadStatusState(statusFilePath), "the session's end");
            Assert.AreEqual(3, killSwitch.BlockCount, "The held change of the block has not landed.");
            Assert.AreEqual(ClientState.Connected, stateAtLanding,
                "The session's way down reached the app before its change of the block landed.");
        }
        finally {
            blockGate.TrySetResult();
        }
    }

    // A new connect's session read as the last one's until it had read its options, so a stop the app asked then, as of a
    // system's start, which the app wrote nothing for, met the last session's state, its end say, and took it for this
    // one's. A connect writes its Initializing at once
    [TestMethod]
    public async Task A_connect_reads_as_initializing_from_its_begin()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        var vpnAdapter = new GatedVpnAdapter();
        vpnAdapter.OpenGate();
        var adapterCount = 0;
        using var device = new TestDevice(TestHelper,
            _ => Interlocked.Increment(ref adapterCount) == 1 ? vpnAdapter : new TestNullVpnAdapter()) {
            KillSwitch = killSwitch
        };

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null, CreateServiceOptions(token, useKillSwitch: true), TestCt);
        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var startOptions = vpnAdapter.StartOptions ?? throw new InvalidOperationException("The adapter has not started.");
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await AssertEqualsWait(2, () => killSwitch.BlockCount, "the connect's block and its re-point");

        // the last session's re-point held at its block, so the switch's turn, which a new connect takes before it reads
        // its options, is held; a forced connect meanwhile
        var blockGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        killSwitch.BlockGate = blockGate.Task;
        try {
            vpnAdapter.RaiseStarted(startOptions);
            await killSwitch.BlockWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var sessionId = service.SessionId;
            var connect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
            await AssertEqualsWait(true, () => service.SessionId != sessionId, "the new connect's session");
            await AssertEqualsWait(ClientState.Initializing, () => ReadStatusState(statusFilePath),
                "the new session's first word");

            blockGate.TrySetResult();
            Assert.IsTrue(await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt));
        }
        finally {
            blockGate.TrySetResult();
        }

        await manager.TryStop();
    }

    // A session's end was written before its tunnel was let go, by its client's own end and by a disconnect: the app took
    // the service for stopped while it still held the tunnel. The tunnel goes back first
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_sessions_end_comes_once_its_tunnel_is_let_go(bool isDisconnect)
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var tunnelLock = new TestVpnTunnelLock();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) { TunnelLock = tunnelLock };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var client = service.Client ?? throw new InvalidOperationException("The service has no client.");
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);

        // the session ends, by a disconnect or its client's own close, the hold's close held; a disconnect's two ends, its
        // own and its client's, are both looked at: the second has found the hold taken by the first
        var holdDisposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tunnelLock.HoldDisposeGate = holdDisposeGate.Task;
        try {
            var end = isDisconnect ? service.Disconnect() : client.DisposeAsync().AsTask();
            await tunnelLock.HoldDisposeWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            if (isDisconnect)
                await service.TunnelReleaseWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

            await AssertEqualsWait(ClientState.Disconnecting, () => ReadStatusState(statusFilePath),
                "the state's disconnecting while the tunnel is let go");

            Assert.AreEqual(ClientState.Disconnecting, (await ReadStatusWait(statusFilePath)).ClientState,
                "The session's end was written before its tunnel was let go.");

            holdDisposeGate.TrySetResult();
            await end.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            await AssertEqualsWait(ClientState.Disposed, () => ReadStatusState(statusFilePath), "the session's end");
            Assert.IsFalse(tunnelLock.IsHeld, "The tunnel is still held.");
        }
        finally {
            holdDisposeGate.TrySetResult();
            tunnelLock.HoldDisposeGate = null;
        }
    }

    // A session whose proxies' statuses could not be saved at its end, as on a disk that refused the write, never wrote
    // its end, nor let the tunnel go, nor stopped the service: the save came first, and its failure skipped the rest; and
    // so did a connector that refuses to tell its status once closed. The end goes on whatever the proxies' part does
    [TestMethod]
    public async Task A_session_ends_where_its_proxies_statuses_cannot_be_saved()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var tunnelLock = new TestVpnTunnelLock();
        var proxyConnector = new UnsavedProxyConnector();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            TunnelLock = tunnelLock,
            ProxyConnectorFactory = () => proxyConnector
        };

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);

        // the connect finds no proxy to go through, and the client's session ends on its own: the service's own end, not
        // a state the manager wrote as the start ran out of its time
        var start = manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        await Assert.ThrowsAsync<UnreachableProxyServerException>(() =>
            start.WaitAsync(TimeSpan.FromSeconds(30), TestCt));

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        await AssertEqualsWait(true, () => service.IsDisposed, "the service's stop", timeout: 10_000);
        var connectionInfo = await ReadStatusWait(statusFilePath);
        Assert.AreEqual(ClientState.Disposed, connectionInfo.ClientState, "The session's end is not in the state.");
        Assert.IsTrue(connectionInfo.Error?.Is<UnreachableProxyServerException>(),
            "The session's end does not tell why it ended.");

        Assert.AreEqual(1, tunnelLock.TakenCount, "The connect took no tunnel.");
        Assert.IsFalse(tunnelLock.IsHeld, "The tunnel is still held.");
        Assert.IsTrue(proxyConnector.ClosedFlushCount > 0, "No save of the proxies' statuses was tried at the end.");
    }

    // A session's end saved its proxies' statuses before it let the tunnel go, so a slow save, as on a busy disk, kept the
    // machine's tunnel held after the adapter was down. The tunnel goes back first, and the save comes with the end
    [TestMethod]
    public async Task A_sessions_tunnel_goes_back_while_its_proxies_statuses_are_saved()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var tunnelLock = new TestVpnTunnelLock();
        var flushGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proxyConnector = new UnsavedProxyConnector { ClosedFlushGate = flushGate.Task };
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            TunnelLock = tunnelLock,
            ProxyConnectorFactory = () => proxyConnector
        };

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);

        // the connect finds no proxy to go through, and the client's session ends on its own, its save held
        var start = manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        try {
            await proxyConnector.ClosedFlushWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            Assert.AreEqual(1, tunnelLock.TakenCount, "The connect took no tunnel.");
            await AssertEqualsWait(false, () => tunnelLock.IsHeld, "the tunnel's release while the save is held");
        }
        finally {
            flushGate.TrySetResult();
        }

        await Assert.ThrowsAsync<UnreachableProxyServerException>(() =>
            start.WaitAsync(TimeSpan.FromSeconds(30), TestCt));
    }

    // A connect's client, the service's but not started yet, read as no service, which a stop took for the end at once;
    // and one closed before its session kept that state, or its connect's, until its end came, while the service still
    // held what an older client's close held. A client not started yet reads as initializing, and one that has ended as
    // disconnecting until its end is written
    [TestMethod]
    public async Task A_client_not_started_reads_as_initializing_and_one_ended_as_disconnecting()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var oldAdapter = new GatedVpnAdapter();
        oldAdapter.OpenGate();
        var adapterCount = 0;
        using var device = new TestDevice(TestHelper,
            _ => Interlocked.Increment(ref adapterCount) == 1 ? oldAdapter : new TestNullVpnAdapter());

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);

        // a forced connect replaces the client, whose close is held at its adapter's, and is held once its own client is
        // the service's, before that client starts
        var disposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientNotificationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldAdapter.DisposeGate = disposeGate.Task;
        service.ClientNotificationGate = clientNotificationGate.Task;
        var connect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            await oldAdapter.DisposeWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            await service.ClientNotificationWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var newClient = service.Client ?? throw new InvalidOperationException("The connect has no client.");
            Assert.AreEqual(ClientState.Initializing, (await ReadStatusWait(statusFilePath)).ClientState,
                "A client not started yet does not read as initializing.");

            // the app stops the service, which closes that client before its session
            var stop = manager.TryStop();
            await AssertEqualsWait(ClientState.Disposed, () => newClient.State, "the new client's close");
            Assert.IsFalse(stop.IsCompleted, "The stop ended while a replaced client still closed.");
            await AssertEndPending(device.VpnServiceConfigFolder, "a replaced client still closed");

            disposeGate.TrySetResult();
            clientNotificationGate.TrySetResult();
            Assert.IsTrue(await stop.WaitAsync(TimeSpan.FromSeconds(30), TestCt), "The stop did not see the end.");
            Assert.AreEqual(ClientState.Disposed, (await ReadStatusWait(statusFilePath)).ClientState,
                "The session's end is not in the state.");
        }
        finally {
            disposeGate.TrySetResult();
            clientNotificationGate.TrySetResult();
        }

        await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
    }

    // A close of the notification that the system refused, as Android may, skipped the service's stop at a session's
    // end: the service and its notification stayed after the VPN ended. The service stops whatever that close did
    [TestMethod]
    public async Task A_service_stops_where_its_notification_cannot_be_closed()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter());
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");

        service.StopNotificationException = new InvalidOperationException("The system refused to take it down.");
        await service.Disconnect().WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        await AssertEqualsWait(true, () => service.IsDisposed, "the service's stop", timeout: 10_000);
    }

    // A start of the last connect's adapter that came late, as a restart's word, while a newer connect was between its
    // block and its adapter, pointed the newer connect's block at the old adapter's routes: that adapter was still the
    // one the service followed. An adapter is followed while its connect is the service's alone
    [TestMethod]
    public async Task A_late_start_of_the_last_adapter_leaves_a_newer_connects_block()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        var oldAdapter = new GatedVpnAdapter();
        oldAdapter.OpenGate();
        var adapterGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterCount = 0;
        using var device = new TestDevice(TestHelper, _ => {
            if (Interlocked.Increment(ref adapterCount) == 1)
                return oldAdapter;

            // the newer connect's adapter is held as it is made, its block up
            adapterWaiting.TrySetResult();
            adapterGate.Task.Wait(CancellationToken.None);
            return new TestNullVpnAdapter();
        }) { KillSwitch = killSwitch };
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        await manager.Start(uiContext: null, CreateServiceOptions(token, useKillSwitch: true), TestCt);
        var service = device.VpnService ?? throw new InvalidOperationException("The service has not started.");
        var oldStartOptions = oldAdapter.StartOptions ??
                              throw new InvalidOperationException("The adapter has not started.");

        await AssertEqualsWait(2, () => killSwitch.BlockCount, "the first connect's block and its re-point");

        var connect = Task.Run(() => service.Connect(forceReconnect: true), TestCt);
        try {
            await adapterWaiting.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
            var blockCount = killSwitch.BlockCount;
            oldAdapter.RaiseStarted(oldStartOptions);
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
            Assert.AreEqual(blockCount, killSwitch.BlockCount, "The old adapter's start re-pointed the newer block.");
        }
        finally {
            adapterGate.TrySetResult();
        }

        Assert.IsTrue(await connect.WaitAsync(TimeSpan.FromSeconds(30), TestCt));
        await manager.TryStop();
    }

    // the state the status file holds, or None while a write of it is under way
    private static ClientState ReadStatusState(string statusFilePath)
    {
        return ReadStatus(statusFilePath)?.ClientState ?? ClientState.None;
    }

    // The status file as the manager reads it, which lets the service write meanwhile: a read that kept the service's
    // write out would fail it, and the state the test waits for would never come; null while a write is under way
    private static ConnectionInfo? ReadStatus(string statusFilePath)
    {
        try {
            using var stream = new FileStream(statusFilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            return JsonSerializer.Deserialize<ConnectionInfo>(stream);
        }
        catch (Exception ex) when (ex is IOException or JsonException) {
            return null;
        }
    }

    // the status file's first read that is not met by a write under way
    private async Task<ConnectionInfo> ReadStatusWait(string statusFilePath)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        while (true) {
            if (ReadStatus(statusFilePath) is { } connectionInfo)
                return connectionInfo;

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeoutCts.Token);
        }
    }

    // The status file and the service's answer to a status request, as the app's manager sends one, tell the session as
    // disconnecting, once the file has come to it, and the answer writes no end; whileText: what the service still holds
    // meanwhile
    private async Task AssertEndPending(string configFolder, string whileText)
    {
        var statusFilePath = Path.Combine(configFolder, ClientOptions.VpnStatusFileName);
        await AssertEqualsWait(ClientState.Disconnecting, () => ReadStatusState(statusFilePath),
            $"the state's disconnecting while {whileText}");

        Assert.AreEqual(ClientState.Disconnecting, (await AskStatus(configFolder)).ClientState,
            $"The service's answer does not tell a disconnecting state while {whileText}.");

        Assert.AreEqual(ClientState.Disconnecting, (await ReadStatusWait(statusFilePath)).ClientState,
            $"The service's answer wrote another state while {whileText}.");
    }

    // the service's answer to a status request, over a channel of its own, within the time given or 30 seconds: its
    // state, where it answered with no error
    private async Task<ConnectionInfo> AskStatus(string configFolder, TimeSpan? timeout = null)
    {
        using var messageClient = new TcpMessageClient(configFolder);
        using var requestStream = new MemoryStream();
        await StreamUtils.WriteObjectAsync(requestStream, nameof(ApiGetConnectionInfoRequest), TestCt);
        await StreamUtils.WriteObjectAsync(requestStream, new ApiGetConnectionInfoRequest(), TestCt);
        var answer = await messageClient.SendAsync(requestStream.ToArray(), TestCt)
            .WaitAsync(timeout ?? TimeSpan.FromSeconds(30), TestCt);

        var response = JsonSerializer.Deserialize<ApiResponse<object>>(answer.Span) ??
                       throw new InvalidOperationException("The service gave no answer.");

        Assert.IsNull(response.ApiError, "The service's answer is an error.");
        return response.ConnectionInfo;
    }

    // the status file's first read that is not met by a write under way, on the caller's thread, as a fake's hook reads
    private static ConnectionInfo ReadStatusNow(string statusFilePath)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true) {
            if (ReadStatus(statusFilePath) is { } connectionInfo)
                return connectionInfo;

            if (stopwatch.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The state could not be read.");

            Thread.Sleep(20);
        }
    }

    // A status request the service answered slowly, within its time, held the channel past the disconnect's 5 seconds,
    // so the disconnect ran out of its time waiting for its turn and was never sent. A stop ends the requests under way
    [TestMethod]
    public async Task A_stop_is_sent_while_a_status_request_waits_for_its_answer()
    {
        using var device = TestHelper.CreateNullDevice();
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Connected), TestCt);

        // the service answers a status request late, and ends at a disconnect
        var statusAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new TcpMessageListener(device.VpnServiceConfigFolder);
        var listenerTask = listener.Start(async (request, cancellationToken) => {
            if (Encoding.UTF8.GetString(request.Span).Contains(nameof(ApiDisconnectRequest))) {
                await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Disposed), cancellationToken);
                disconnectAsked.TrySetResult();
                return CreateAnswer(ClientState.Disposed);
            }

            statusAsked.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return CreateAnswer(ClientState.Connected);
        }, TestCt);

        var refresh = manager.RefreshState(TestCt);
        await statusAsked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        var stop = manager.TryStop(TimeSpan.FromSeconds(2));

        await disconnectAsked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.IsTrue(await stop.WaitAsync(TimeSpan.FromSeconds(10), TestCt), "The stop did not see the end.");
        await refresh.WaitAsync(TimeSpan.FromSeconds(10), TestCt);

        listener.Dispose();
        await listenerTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
    }

    // A status request a stop ended failed as the service gone where that end cut its connection, and the stop took it
    // for the service's end while the service still tore down. A request a stop ends reads as ended, whatever its
    // channel threw
    [TestMethod]
    public async Task A_stop_waits_for_the_end_where_it_cut_a_status_request()
    {
        var messageClient = new StopCutMessageClient();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            MessageClientFactory = () => messageClient
        };

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Connected), TestCt);

        // the disconnect's answer held until the cut request has ended and its state is read
        var refresh = manager.RefreshState(TestCt);
        await messageClient.StatusWaiting.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        var stop = manager.TryStop(TimeSpan.FromSeconds(5));
        try {
            await messageClient.DisconnectAsked.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
            await refresh.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
            Assert.AreEqual(ClientState.Connected, ReadStatusState(statusFilePath),
                "The cut status request ended the service in the state.");
        }
        finally {
            messageClient.DisconnectGate.TrySetResult();
        }

        // the service tears down, then ends
        Assert.IsFalse(await Task.WhenAny(stop, Task.Delay(TimeSpan.FromMilliseconds(500), TestCt)) == stop,
            "The stop ended while the service tore down.");

        messageClient.State = ClientState.Disposed;
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Disposed), TestCt);
        Assert.IsTrue(await stop.WaitAsync(TimeSpan.FromSeconds(10), TestCt), "The stop did not see the end.");
    }

    // A stop with no time limit whose disconnect the service never answered waited on after the manager's close, which
    // ended the other requests alone, where the channel's close ends no request under way, as iOS's. The close ends it
    [TestMethod]
    public async Task A_stop_ends_with_its_managers_close()
    {
        var messageClient = new UnansweredDisconnectMessageClient();
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            MessageClientFactory = () => messageClient
        };

        Directory.CreateDirectory(device.VpnServiceConfigFolder);
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        await File.WriteAllTextAsync(statusFilePath, StatusJson(ClientState.Connected), TestCt);
        var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);

        var stop = manager.TryStop(Timeout.InfiniteTimeSpan);
        await messageClient.DisconnectAsked.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        manager.Dispose();
        Assert.IsFalse(await stop.WaitAsync(TimeSpan.FromSeconds(10), TestCt), "The stop saw an end.");
    }

    // A reconfigure that turned on a kill switch the device does not have did nothing and said nothing, where a connect
    // with it fails. It fails too
    [TestMethod]
    public async Task A_reconfigure_that_turns_on_a_kill_switch_the_device_lacks_fails()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        using var device = TestHelper.CreateNullDevice();
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null);
        await manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        var reconfiguredOptions = TestHelper.CreateClientOptions(token);
        reconfiguredOptions.UseKillSwitch = true;
        var ex = await Assert.ThrowsAsync<Exception>(() =>
            manager.Reconfigure(new VpnServiceOptions { ClientOptions = reconfiguredOptions }, TestCt));

        StringAssert.Contains(ex.Message, "kill switch");
    }

    // A switch turned on while a connect waited for the tunnel was taken for done, as no block was up yet; the connect
    // then set up the block by the settings it had read before, without the switch, and kept to it. The connect takes
    // the switch it was last asked for
    [TestMethod]
    public async Task A_switch_turned_on_while_the_connect_waits_for_the_tunnel_goes_up()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        var takeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tunnelLock = new TestVpnTunnelLock { TakeGate = takeGate.Task };
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            KillSwitch = killSwitch,
            TunnelLock = tunnelLock
        };

        using var manager = new VpnServiceManager(device, eventWatcherInterval: null, IdleJobRunner);

        var start = manager.Start(uiContext: null,
            new VpnServiceOptions { ClientOptions = TestHelper.CreateClientOptions(token) }, TestCt);

        await tunnelLock.TakeWaiting.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.UseKillSwitch = true;
        await manager.Reconfigure(new VpnServiceOptions { ClientOptions = clientOptions }, TestCt);
        takeGate.TrySetResult();

        await start.WaitAsync(TimeSpan.FromSeconds(30), TestCt);
        Assert.IsTrue(killSwitch.IsUp, "The switch turned on during the connect is off.");
        await manager.TryStop();
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

    // a reconfigure's settings, the defaults but for the kill switch
    private static ClientReconfigureParams CreateReconfigureParams(bool useKillSwitch)
    {
        return new ClientReconfigureParams {
            DropQuic = false,
            DropUdp = false,
            UseTcpForwarding = true,
            AllowAnonymousTracker = false,
            ChannelProtocol = ChannelProtocol.Tcp,
            UnroutedIpMode = SplitUnsupportedIpMode.Block,
            UnsupportedIpV6Mode = SplitUnsupportedIpMode.Block,
            UseKillSwitch = useKillSwitch,
            ProxyOptions = new ProxyOptions()
        };
    }

    // a connect's options, with the kill switch as given
    private VpnServiceOptions CreateServiceOptions(Token token, bool useKillSwitch)
    {
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.UseKillSwitch = useKillSwitch;
        return new VpnServiceOptions { ClientOptions = clientOptions };
    }

    // the service's answer to a request, with its state
    private static Memory<byte> CreateAnswer(ClientState clientState)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new ApiResponse<object> {
            ConnectionInfo = JsonSerializer.Deserialize<ConnectionInfo>(StatusJson(clientState)) ??
                             throw new InvalidOperationException("No status."),
            ApiError = null,
            Result = null
        });
    }

    // a channel to a connected service that, once asked to disconnect, tears down until the test ends it: its first
    // status request waits until it is ended, and then fails as a connection that end cut does
    private sealed class StopCutMessageClient : IMessageClient
    {
        private readonly TaskCompletionSource _statusWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disconnectAsked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _statusCount;

        public Task StatusWaiting => _statusWaiting.Task;
        public Task DisconnectAsked => _disconnectAsked.Task;

        // the disconnect's answer waits for it
        public TaskCompletionSource DisconnectGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // the state the service answers with from the disconnect on
        public ClientState State { get; set; } = ClientState.Disconnecting;

        public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
        {
            if (Encoding.UTF8.GetString(request.Span).Contains(nameof(ApiDisconnectRequest))) {
                _disconnectAsked.TrySetResult();
                await DisconnectGate.Task.WaitAsync(cancellationToken);
                return CreateAnswer(State);
            }

            if (Interlocked.Increment(ref _statusCount) > 1)
                return CreateAnswer(State);

            _statusWaiting.TrySetResult();
            try {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CreateAnswer(ClientState.Connected);
            }
            catch (OperationCanceledException ex) {
                throw new VpnServiceUnreachableException("VpnService connection failed before its answer.",
                    new IOException("The connection was cut.", ex));
            }
        }

        public void Dispose()
        {
        }
    }

    // a channel to a connected service that takes a disconnect and never answers it, and whose close ends no request
    // under way, as iOS's
    private sealed class UnansweredDisconnectMessageClient : IMessageClient
    {
        private readonly TaskCompletionSource _disconnectAsked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DisconnectAsked => _disconnectAsked.Task;

        public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
        {
            if (!Encoding.UTF8.GetString(request.Span).Contains(nameof(ApiDisconnectRequest)))
                return CreateAnswer(ClientState.Connected);

            _disconnectAsked.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreateAnswer(ClientState.Disposed);
        }

        public void Dispose()
        {
        }
    }

    // a channel that answers each request with a Connected state, but for the one of the number given, whose connection
    // fails after it went, before its answer; it sends no request again of its own
    private sealed class CutAnswerMessageClient(int cutRequestNumber) : IMessageClient
    {
        private int _requestCount;

        public int RequestCount => _requestCount;

        public Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
        {
            return Interlocked.Increment(ref _requestCount) == cutRequestNumber
                ? Task.FromException<Memory<byte>>(new VpnServiceUnreachableException(
                    "VpnService connection failed before its answer.",
                    new IOException("The connection failed after the request was taken.")))
                : Task.FromResult(CreateAnswer(ClientState.Connected));
        }

        public void Dispose()
        {
        }
    }

    // the proxies of a client whose statuses cannot be saved, as on a disk that refuses the write, and which refuses to
    // tell its status once closed: with no endpoint to go through, the client's connect fails, and its session ends on
    // its own
    private sealed class UnsavedProxyConnector : IProxyConnector
    {
        private readonly TaskCompletionSource _closedFlushWaiting =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _closedFlushCount;
        private bool _isClosed;

        // the saves tried once the connector was closed, as at the session's end, each of them refused
        public int ClosedFlushCount => Volatile.Read(ref _closedFlushCount);

        // where a test holds a save tried once the connector was closed, as a slow disk's: such a save waits for it first
        public Task? ClosedFlushGate { get; init; }

        // completes once such a save waits at the gate
        public Task ClosedFlushWaiting => _closedFlushWaiting.Task;

        public bool IsEnabled => true;
        public bool UseRecentSucceeded { get; set; }
        public ProgressStatus? Progress => null;

        public ProxyConnectorStatus Status => Volatile.Read(ref _isClosed)
            ? throw new ObjectDisposedException(nameof(UnsavedProxyConnector))
            : new ProxyConnectorStatus {
                AutoUpdate = false,
                SessionStatus = new ProxySessionStatus(),
                IsAnySucceeded = false,
                SucceededServerCount = 0,
                FailedServerCount = 0,
                UnknownServerCount = 0,
                DisabledServerCount = 0
            };

        public Task<bool> WaitForEndPoints(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<ProxyConnection> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
            RemoteCertificateValidationCallback? serverCertificateValidation, Action? onAttempt,
            CancellationToken cancellationToken)
        {
            return Task.FromException<ProxyConnection>(new IOException("There is no proxy to go through."));
        }

        public void RecordFailed(TcpClient tcpClient, Exception ex)
        {
        }

        public Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
            RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task Flush(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _isClosed)) {
                Interlocked.Increment(ref _closedFlushCount);
                if (ClosedFlushGate is { } closedFlushGate) {
                    _closedFlushWaiting.TrySetResult();
                    await closedFlushGate.WaitAsync(cancellationToken);
                }
            }

            throw new IOException("The disk refused the proxies' statuses.");
        }

        public ValueTask DisposeAsync()
        {
            Volatile.Write(ref _isClosed, true);
            return ValueTask.CompletedTask;
        }
    }
}
