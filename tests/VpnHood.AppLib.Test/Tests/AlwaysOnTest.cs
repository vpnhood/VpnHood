using System.Net;
using System.Security.Authentication;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.AlwaysOn;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;

namespace VpnHood.AppLib.Test.Tests;

// Always on where the app keeps it itself, as a desktop's service: it connects as the app starts, tries a
// failure to reach a working server again after a pause that grows, and stops for anything else and for a
// person's disconnect; a revoke stands across a restart until a person connects.
[TestClass]
public class AlwaysOnTest : TestAppBase
{
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(300);

    [TestMethod]
    public async Task The_start_connects_when_on_and_turning_it_on_connects_nothing()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, _) = await SaveSettings(token, alwaysOn: false);

        // off: the start connects nothing, and turning the switch on connects nothing either
        await using (var app = CreateApp(storagePath)) {
            Assert.AreEqual(AlwaysOnSupport.App, app.Features.AlwaysOnSupport);
            Assert.AreEqual(AlwaysOnStatus.Off, app.State.AlwaysOnState?.Status);
            await Task.Delay(TimeSpan.FromSeconds(1), TestCt); // the app's start, a task of its own, has looked
            app.UserSettings.AlwaysOn = true;
            app.SettingsService.Save();
            await Task.Delay(TimeSpan.FromSeconds(1), TestCt);
            Assert.AreEqual(AppConnectionState.None, app.State.ConnectionState);
            Assert.AreEqual(AlwaysOnStatus.On, app.State.AlwaysOnState?.Status);
        }

        // on: the next start connects
        await using var app2 = CreateApp(storagePath);
        await app2.WaitForState(AppConnectionState.Connected);
        Assert.AreEqual(AlwaysOnStatus.On, app2.State.AlwaysOnState?.Status);
    }

    [TestMethod]
    public async Task Maintenance_is_tried_again_after_a_pause_that_grows()
    {
        var accessManager = TestHttpAccessManager.Create(TestHelper.CreateAccessManager());
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, _) = await SaveSettings(token, alwaysOn: true);

        // down for maintenance: tried again after the first pause, then after the longer second
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;
        await using var app = CreateApp(storagePath, retryPauses: [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)]);
        var first = await WaitForRetry(app, previousRetryTime: null);
        Assert.AreEqual(nameof(MaintenanceException), first.Error?.TypeName);
        Assert.IsTrue(first.RetryTime <= DateTime.UtcNow + TimeSpan.FromSeconds(1), "The first pause is the shortest.");
        var second = await WaitForRetry(app, first.RetryTime);
        Assert.IsTrue(second.RetryTime >= DateTime.UtcNow + TimeSpan.FromSeconds(2), "The second pause is longer.");

        // back: the next try connects
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = null;
        await app.WaitForState(AppConnectionState.Connected, timeout: 15000);
        Assert.AreEqual(AlwaysOnStatus.On, app.State.AlwaysOnState?.Status);
    }

    // With traffic over a capturing adapter (WinDivert, an administrator's), as an idle client never finds the
    // server gone.
    [TestMethod]
    public async Task A_server_out_of_reach_is_tried_again_and_a_session_that_held_starts_the_pauses_over()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        var token = TestHelper.CreateAccessToken(accessManager);
        var (storagePath, _) = await SaveSettings(token, alwaysOn: true);

        // no server yet: a failure, so that the next pause would be the long one
        await using var app = CreateApp(storagePath, capture: true,
            retryPauses: [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)],
            retryPausesResetTime: TimeSpan.FromSeconds(1));
        var first = await WaitForRetry(app, previousRetryTime: null);
        Assert.AreEqual(nameof(UnreachableServerException), first.Error?.TypeName);
        await using var server = await TestHelper.CreateServer(accessManager);
        await app.WaitForState(AppConnectionState.Connected, timeout: 10000);

        // the session holds past the reset time, then the server goes and the network ends the session (the
        // transport's SessionTimeout): tried again, after the short pause
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestCt);
        // ReSharper disable once DisposeOnUsingVariable
        await server.DisposeAsync();
        var retry = await WaitForRetry(app, previousRetryTime: null,
            whileWaiting: () => TestHelper.Test_Https(throwError: false, timeout: TimeSpan.FromMilliseconds(100)));
        Assert.IsTrue(retry.RetryTime <= DateTime.UtcNow + TimeSpan.FromSeconds(1),
            $"A held session starts the pauses over. Error: {retry.Error?.TypeName}");
    }

    [TestMethod]
    public async Task A_refusal_stops_it()
    {
        await using var server = await TestHelper.CreateServer();
        server.ServerHost.MinClientProtocolVersion = 1000; // a version the server no longer takes
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, _) = await SaveSettings(token, alwaysOn: true);

        await using var app = CreateApp(storagePath);
        await AssertEqualsWait(AlwaysOnStatus.Stopped, () => app.State.AlwaysOnState?.Status);
        Assert.AreEqual(nameof(SessionErrorCode.UnsupportedClient),
            app.State.AlwaysOnState?.Error?.Data.GetValueOrDefault("ErrorCode"));

        // and stays so: nothing tries again after the pause
        await Task.Delay(Pause * 3, TestCt);
        Assert.AreEqual(AlwaysOnStatus.Stopped, app.State.AlwaysOnState?.Status);
        Assert.AreEqual(AppConnectionState.None, app.State.ConnectionState);
    }

    [TestMethod]
    public async Task A_persons_disconnect_stops_it_until_a_persons_connect()
    {
        var accessManager = TestHttpAccessManager.Create(TestHelper.CreateAccessManager());
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, vpnProfileId) = await SaveSettings(token, alwaysOn: true);

        // a disconnect while always on waits to try again
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;
        await using var app = CreateApp(storagePath, retryPauses: [TimeSpan.FromSeconds(1)]);
        await WaitForRetry(app, previousRetryTime: null);
        await app.Disconnect();
        Assert.AreEqual(AlwaysOnStatus.Stopped, app.State.AlwaysOnState?.Status);
        Assert.IsNull(app.State.AlwaysOnState?.Error);

        // the server is back, and nothing tries it
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = null;
        await Task.Delay(TimeSpan.FromSeconds(2), TestCt);
        Assert.AreEqual(AppConnectionState.None, app.State.ConnectionState);

        // a person's connect starts it again, and a disconnect of a session stops it again
        await app.Connect(vpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);
        Assert.AreEqual(AlwaysOnStatus.On, app.State.AlwaysOnState?.Status);
        await app.Disconnect();
        await app.WaitForState(AppConnectionState.None);
        await Task.Delay(Pause * 3, TestCt);
        Assert.AreEqual(AppConnectionState.None, app.State.ConnectionState);
        Assert.AreEqual(AlwaysOnStatus.Stopped, app.State.AlwaysOnState?.Status);
    }

    [TestMethod]
    public async Task A_revoke_stands_across_a_restart_until_a_persons_connect()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, vpnProfileId) = await SaveSettings(token, alwaysOn: true);
        var tunnelLock = new TestVpnTunnelLock();

        // another app takes the tunnel: always on stops
        await using var app = CreateApp(storagePath, tunnelLock);
        await app.WaitForState(AppConnectionState.Connected);
        using var otherHold = await tunnelLock.Take(new RecordingVpnTunnelHolder(), TestCt);
        await app.WaitForState(AppConnectionState.None);
        await AssertEqualsWait(AlwaysOnStatus.Stopped, () => app.State.AlwaysOnState?.Status);
        Assert.AreEqual(nameof(VpnServiceRevokedException), app.State.AlwaysOnState?.Error?.TypeName);
        // ReSharper disable once DisposeOnUsingVariable
        await app.DisposeAsync();

        // the restart leaves the tunnel to the other app
        await using var app2 = CreateApp(storagePath, tunnelLock);
        await AssertEqualsWait(AlwaysOnStatus.Stopped, () => app2.State.AlwaysOnState?.Status);
        await Task.Delay(Pause * 3, TestCt);
        Assert.AreEqual(AppConnectionState.None, app2.State.ConnectionState);

        // a person's connect takes it back, and ends the revoke
        await app2.Connect(vpnProfileId, cancellationToken: TestCt);
        await app2.WaitForState(AppConnectionState.Connected);
        Assert.AreEqual(AlwaysOnStatus.On, app2.State.AlwaysOnState?.Status);
    }

    // The service stops right after another app took the tunnel, before the app has seen why its session ended.
    [TestMethod]
    public async Task A_revoke_the_app_had_not_seen_yet_stands_across_a_restart()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, _) = await SaveSettings(token, alwaysOn: true);
        var tunnelLock = new TestVpnTunnelLock();
        var otherHolder = new RecordingVpnTunnelHolder();

        await using var app = CreateApp(storagePath, tunnelLock);
        await app.WaitForState(AppConnectionState.Connected);
        using var otherHold = await tunnelLock.Take(otherHolder, TestCt);
        // ReSharper disable once DisposeOnUsingVariable
        await app.DisposeAsync();

        // the restart leaves the tunnel to the other app
        await using var app2 = CreateApp(storagePath, tunnelLock);
        await AssertEqualsWait(AlwaysOnStatus.Stopped, () => app2.State.AlwaysOnState?.Status);
        Assert.AreEqual(nameof(VpnServiceRevokedException), app2.State.AlwaysOnState?.Error?.TypeName);
        await Task.Delay(Pause * 3, TestCt);
        Assert.AreEqual(AppConnectionState.None, app2.State.ConnectionState);
        Assert.IsFalse(otherHolder.Asked.IsCompleted, "The other app keeps the tunnel.");
    }

    // Always on chose to connect - a retry fell due, say - and something came before its connect began.
    [TestMethod]
    public async Task Always_ons_connect_gives_way_to_a_persons_disconnect_or_connect_that_came_after_it()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, vpnProfileId) = await SaveSettings(token, alwaysOn: true);

        // a person's disconnect: it stays disconnected
        var device = CreateDevice();
        await using var app = CreateApp(storagePath, device: device);
        await app.WaitForState(AppConnectionState.Connected);
        await app.Disconnect();
        await app.Connect(uiContext: null, new ConnectOptions { IsAlwaysOn = true }, TestCt);
        Assert.AreEqual(AppConnectionState.None, app.State.ConnectionState);
        Assert.AreEqual(AlwaysOnStatus.Stopped, app.State.AlwaysOnState?.Status);

        // a person's connect, slow to start: it goes on
        device.StartServiceDelay = TimeSpan.FromSeconds(2);
        var personsConnect = app.Connect(vpnProfileId, cancellationToken: TestCt);
        await AssertEqualsWait(AppConnectionState.Initializing, () => app.State.ConnectionState);
        await app.Connect(uiContext: null, new ConnectOptions { IsAlwaysOn = true }, TestCt);
        await personsConnect;
        Assert.AreEqual(AppConnectionState.Connected, app.State.ConnectionState);
    }

    [TestMethod]
    public async Task A_plan_change_during_a_try_that_stops_makes_it_try_once_more()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        token.IsPublic = true; // a CONNECT-style profile: premium comes from a code alone
        var (storagePath, vpnProfileId) = await SaveSettings(token, alwaysOn: true);
        var premium = new AppPremiumOptions { Features = [AppFeature.AlwaysOn], AllowImportAccessCode = true };

        // the start's try, under a free plan, which the VPN service refuses, is slow to reach it, and a code
        // comes meanwhile
        var device = CreateDevice();
        device.StartServiceDelay = TimeSpan.FromSeconds(2);
        await using var app = CreateApp(storagePath, premium: premium, device: device);
        await AssertEqualsWait(AppConnectionState.Initializing, () => app.State.ConnectionState);
        var accessCode = TestAppHelper.BuildAccessCode();
        accessManager.AccessCodes.Add(accessCode, TestHelper.CreateAccessToken(server).TokenId);
        app.VpnProfileService.Update(vpnProfileId, new VpnProfileUpdateParams { AccessCode = accessCode });
        device.StartServiceDelay = TimeSpan.Zero;

        // the free try is refused, and always on tries once more under the code, and connects
        await app.WaitForState(AppConnectionState.Connected, timeout: 15000);
        Assert.AreEqual(AlwaysOnStatus.On, app.State.AlwaysOnState?.Status);
    }

    [TestMethod]
    public async Task Without_its_plan_it_is_refused_and_a_plan_change_tries_once_more()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        token.IsPublic = true; // a CONNECT-style profile: premium comes from a code alone
        var (storagePath, vpnProfileId) = await SaveSettings(token, alwaysOn: true);
        var premium = new AppPremiumOptions { Features = [AppFeature.AlwaysOn], AllowImportAccessCode = true };

        // a free profile: the VPN service refuses always on's connect, which stops it
        await using var app = CreateApp(storagePath, premium: premium);
        await AssertEqualsWait(AlwaysOnStatus.Stopped, () => app.State.AlwaysOnState?.Status);
        Assert.AreEqual(nameof(PremiumOnlyException), app.State.AlwaysOnState?.Error?.TypeName);

        // a code makes it premium: always on tries once more, and connects
        var accessCode = TestAppHelper.BuildAccessCode();
        accessManager.AccessCodes.Add(accessCode, TestHelper.CreateAccessToken(server).TokenId);
        app.VpnProfileService.Update(vpnProfileId, new VpnProfileUpdateParams { AccessCode = accessCode });
        await app.WaitForState(AppConnectionState.Connected, timeout: 10000);
        Assert.AreEqual(AlwaysOnStatus.On, app.State.AlwaysOnState?.Status);
    }

    [TestMethod]
    public async Task A_persons_connect_takes_over_from_always_ons_connect_on_its_way()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, vpnProfileId) = await SaveSettings(token, alwaysOn: true);

        // always on's start connect waits for a VPN service slow to start, and a person connects meanwhile
        var device = CreateDevice();
        device.StartServiceDelay = TimeSpan.FromSeconds(3);
        await using var app = CreateApp(storagePath, device: device);
        await AssertEqualsWait(AppConnectionState.Initializing, () => app.State.ConnectionState);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt); // the slow start has begun its wait
        device.StartServiceDelay = TimeSpan.Zero;
        await app.Connect(vpnProfileId, cancellationToken: TestCt);

        // the older connect's end, which the person's cancelled, leaves the person's session alone
        await Task.Delay(TimeSpan.FromSeconds(4), TestCt);
        Assert.AreEqual(AppConnectionState.Connected, app.State.ConnectionState);
        Assert.AreEqual(AlwaysOnStatus.On, app.State.AlwaysOnState?.Status);
    }

    [TestMethod]
    public async Task A_diagnose_connects_session_is_sorted_by_how_it_ended()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, vpnProfileId) = await SaveSettings(token, alwaysOn: true);
        var tunnelLock = new TestVpnTunnelLock();

        // a person's diagnose connect after the start's
        await using var app = CreateApp(storagePath, tunnelLock);
        await app.WaitForState(AppConnectionState.Connected);
        await app.Connect(vpnProfileId, diagnose: true, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // another app takes the tunnel: always on stops for the revoke, not for the diagnose's "no error found"
        using var otherHold = await tunnelLock.Take(new RecordingVpnTunnelHolder(), TestCt);
        await AssertEqualsWait(AlwaysOnStatus.Stopped, () => app.State.AlwaysOnState?.Status);
        Assert.AreEqual(nameof(VpnServiceRevokedException), app.State.AlwaysOnState?.Error?.TypeName);
    }

    [TestMethod]
    public async Task A_connect_its_caller_gave_up_on_is_tried_again()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, vpnProfileId) = await SaveSettings(token, alwaysOn: true);

        // after the start's connect, a person's, slow to start, whose caller gives up on it, as a window's
        // timed-out request does
        var device = CreateDevice();
        await using var app = CreateApp(storagePath, device: device, retryPauses: [TimeSpan.FromSeconds(1)]);
        await app.WaitForState(AppConnectionState.Connected);
        device.StartServiceDelay = TimeSpan.FromSeconds(3);
        using var callerCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        callerCts.CancelAfter(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            app.Connect(vpnProfileId, cancellationToken: callerCts.Token));

        // tried again, and connected
        device.StartServiceDelay = TimeSpan.Zero;
        var retry = await WaitForRetry(app, previousRetryTime: null);
        Assert.IsTrue(retry.Error?.TypeName is nameof(OperationCanceledException) or nameof(TaskCanceledException),
            $"Error: {retry.Error?.TypeName}");
        await app.WaitForState(AppConnectionState.Connected, timeout: 10000);
    }

    [TestMethod]
    public async Task An_end_with_no_reason_stops_it_as_an_error_does()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, _) = await SaveSettings(token, alwaysOn: true);

        // the VPN service ends the session by itself, giving no reason
        var device = CreateDevice();
        await using var app = CreateApp(storagePath, device: device);
        await app.WaitForState(AppConnectionState.Connected);
        var vpnService = device.VpnService ?? throw new InvalidOperationException("The VPN service is not running.");
        vpnService.OnStop();

        // stopped with a reason, so it reads as no person's disconnect
        await AssertEqualsWait(AlwaysOnStatus.Stopped, () => app.State.AlwaysOnState?.Status);
        Assert.AreEqual(nameof(VpnServiceException), app.State.AlwaysOnState?.Error?.TypeName);
    }

    [TestMethod]
    public async Task An_adapter_that_stopped_on_its_own_is_tried_again()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var (storagePath, _) = await SaveSettings(token, alwaysOn: true);
        var adapters = new List<BreakableVpnAdapter>();
        var device = new TestDevice(TestAppHelper, _ => {
            var adapter = new BreakableVpnAdapter();
            adapters.Add(adapter);
            return adapter;
        }) { IsAlwaysOnSupported = true };

        // the session's adapter breaks, as one whose interface is disabled or gone, and stops itself
        await using var app = CreateApp(storagePath, device: device, retryPauses: [TimeSpan.FromSeconds(1)]);
        await app.WaitForState(AppConnectionState.Connected);
        adapters.Single().Break();

        // tried again, and the next connect's new adapter connects
        var retry = await WaitForRetry(app, previousRetryTime: null);
        Assert.AreEqual(nameof(VpnAdapterFailedException), retry.Error?.TypeName);
        await app.WaitForState(AppConnectionState.Connected, timeout: 10000);
        Assert.HasCount(2, adapters);
    }

    // What the cases above do not reach: the app's own VPN service out of reach, a broken connection or
    // handshake and a connect its caller gave up on are tried again, and so is an access error once the
    // session was up, from a server that lost it; at a connect it is a refused key, as an HTTP 401 is.
    [TestMethod]
    public void What_is_tried_again()
    {
        ApiError[] triedAgain = [
            new VpnServiceUnreachableException("unreachable").ToApiError(),
            new VpnServiceNotReadyException("not ready").ToApiError(),
            new VpnServiceTimeoutException("timeout") { TimeoutDuration = TimeSpan.FromSeconds(30) }.ToApiError(),
            new IOException().ToApiError(),
            new AuthenticationException().ToApiError(),
            new OperationCanceledException().ToApiError(),
            new TaskCanceledException().ToApiError()
        ];
        foreach (var error in triedAgain)
            Assert.IsTrue(AppAlwaysOnService.IsTriedAgain(error, sessionWasUp: false), error.TypeName);

        var accessError = new SessionException(SessionErrorCode.AccessError).ToApiError();
        Assert.IsTrue(AppAlwaysOnService.IsTriedAgain(accessError, sessionWasUp: true));
        Assert.IsFalse(AppAlwaysOnService.IsTriedAgain(accessError, sessionWasUp: false));

        ApiError[] stopping = [
            new UnauthorizedAccessException().ToApiError(),
            new SessionException(SessionErrorCode.SessionSuppressedBy).ToApiError(),
            new InvalidOperationException().ToApiError()
        ];
        foreach (var error in stopping)
            Assert.IsFalse(AppAlwaysOnService.IsTriedAgain(error, sessionWasUp: true), error.TypeName);
    }

    // The app as a desktop's service runs it, with short pauses. Made by Init alone: its always on connects as
    // it starts, while the helper's CreateClientApp would still be writing the settings SaveSettings has saved.
    private VpnHoodApp CreateApp(string storagePath, TestVpnTunnelLock? tunnelLock = null,
        AppPremiumOptions? premium = null, IReadOnlyList<TimeSpan>? retryPauses = null,
        TimeSpan? retryPausesResetTime = null, bool capture = false, TestDevice? device = null)
    {
        var appOptions = TestAppHelper.CreateAppOptions(storagePath: storagePath);
        appOptions.AlwaysOnOptions = new AppAlwaysOnOptions {
            RetryPauses = retryPauses ?? [Pause],
            RetryPausesResetTime = retryPausesResetTime ?? TimeSpan.FromMinutes(10)
        };
        if (premium != null)
            appOptions.Premium = premium;

        return VpnHoodApp.Init(device ?? CreateDevice(tunnelLock, capture), appOptions);
    }

    // a desktop's device, which keeps always on itself
    private TestDevice CreateDevice(TestVpnTunnelLock? tunnelLock = null, bool capture = false)
    {
        var adapterOptions = TestHelper.CreateTestVpnAdapterOptions();
        return new TestDevice(TestAppHelper,
            _ => capture ? new TestVpnAdapter(adapterOptions) : new TestNullVpnAdapter()) {
            IsAlwaysOnSupported = true,
            TunnelLock = tunnelLock
        };
    }

    // What a person set before the service started: the profile and the switch. The app that saves them
    // keeps no always on itself (the test device's default), so it connects nothing meanwhile.
    private async Task<(string StoragePath, Guid VpnProfileId)> SaveSettings(Token token, bool alwaysOn)
    {
        var storagePath = Path.Combine(TestHelper.WorkingPath, "AppData_" + Guid.CreateVersion7());
        await using var app = TestAppHelper.CreateClientApp(TestAppHelper.CreateAppOptions(storagePath: storagePath));
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        app.UserSettings.VpnProfileId = vpnProfile.VpnProfileId;
        app.UserSettings.AlwaysOn = alwaysOn;
        app.SettingsService.Save();
        return (storagePath, vpnProfile.VpnProfileId);
    }

    // the next wait after a failure: one whose retry time is not the one given
    private async Task<AlwaysOnState> WaitForRetry(VpnHoodApp app, DateTime? previousRetryTime,
        Func<Task>? whileWaiting = null)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline) {
            var alwaysOnState = app.State.AlwaysOnState;
            if (alwaysOnState is { Status: AlwaysOnStatus.Waiting } && alwaysOnState.RetryTime != previousRetryTime)
                return alwaysOnState;

            if (whileWaiting != null)
                await whileWaiting();
            await Task.Delay(20, TestCt);
        }

        throw new TimeoutException("Always on did not wait to try again.");
    }

    // A null adapter the test breaks: from then on each read fails, until the adapter stops itself on the
    // errors and tells its owner (IVpnAdapter.Failed), as one whose interface went does.
    private sealed class BreakableVpnAdapter() : NullVpnAdapter(autoDisposePackets: true, blocking: false)
    {
        private readonly ManualResetEventSlim _brokenEvent = new(false);

        public void Break()
        {
            _brokenEvent.Set();
        }

        protected override void WaitForTunRead()
        {
            _brokenEvent.Wait();
        }

        protected override bool ReadPacket(byte[] buffer)
        {
            return _brokenEvent.IsSet ? throw new IOException("The test broke the adapter.") : false;
        }

        protected override void DisposeManaged()
        {
            _brokenEvent.Set(); // the reader's wait ends; its run is over by now
            base.DisposeManaged();
            _brokenEvent.Dispose();
        }
    }
}
