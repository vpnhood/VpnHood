using System.Net;
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
        IDisposable otherHold;
        await using (var app = CreateApp(storagePath, tunnelLock)) {
            await app.WaitForState(AppConnectionState.Connected);
            otherHold = await tunnelLock.Take(new RecordingVpnTunnelHolder(), TestCt);
            await app.WaitForState(AppConnectionState.None);
            await AssertEqualsWait(AlwaysOnStatus.Stopped, () => app.State.AlwaysOnState?.Status);
            Assert.AreEqual(nameof(VpnServiceRevokedException), app.State.AlwaysOnState?.Error?.TypeName);
        }

        // the restart leaves the tunnel to the other app
        await using var app2 = CreateApp(storagePath, tunnelLock);
        await AssertEqualsWait(AlwaysOnStatus.Stopped, () => app2.State.AlwaysOnState?.Status);
        await Task.Delay(Pause * 3, TestCt);
        Assert.AreEqual(AppConnectionState.None, app2.State.ConnectionState);

        // a person's connect takes it back, and ends the revoke
        await app2.Connect(vpnProfileId, cancellationToken: TestCt);
        await app2.WaitForState(AppConnectionState.Connected);
        Assert.AreEqual(AlwaysOnStatus.On, app2.State.AlwaysOnState?.Status);
        otherHold.Dispose();
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

    // The app as a desktop's service runs it, with short pauses. Made by Init alone: its always on connects as
    // it starts, while the helper's CreateClientApp would still be writing the settings SaveSettings has saved.
    private VpnHoodApp CreateApp(string storagePath, TestVpnTunnelLock? tunnelLock = null,
        AppPremiumOptions? premium = null, IReadOnlyList<TimeSpan>? retryPauses = null,
        TimeSpan? retryPausesResetTime = null, bool capture = false)
    {
        var appOptions = TestAppHelper.CreateAppOptions(storagePath: storagePath);
        appOptions.AlwaysOnOptions = new AppAlwaysOnOptions {
            RetryPauses = retryPauses ?? [Pause],
            RetryPausesResetTime = retryPausesResetTime ?? TimeSpan.FromMinutes(10)
        };
        if (premium != null)
            appOptions.Premium = premium;

        var adapterOptions = TestHelper.CreateTestVpnAdapterOptions();
        var device = new TestDevice(TestAppHelper,
            _ => capture ? new TestVpnAdapter(adapterOptions) : new TestNullVpnAdapter()) {
            IsAlwaysOnSupported = true,
            TunnelLock = tunnelLock
        };
        return VpnHoodApp.Init(device, appOptions);
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
}
