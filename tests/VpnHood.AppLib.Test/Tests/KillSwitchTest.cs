using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Utils;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test.Device;

namespace VpnHood.AppLib.Test.Tests;

// The kill switch as the app and its VPN service run it (IVpnKillSwitch), the OS's block played in memory:
// up before a connect's adapter, pointed at each adapter start, kept through an error stop, lifted by a
// person's disconnect, the switch turned off or another app's take of the tunnel, and fail-closed; kept while
// disconnected where that is on.
[TestClass]
public class KillSwitchTest : TestAppBase
{
    [TestMethod]
    public async Task A_connect_blocks_and_a_persons_disconnect_lifts_it()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        await using var app = CreateApp(CreateDevice(killSwitch), killSwitchOn: true);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        Assert.IsTrue(killSwitch.IsUp);

        // the adapter's start points it at the session: what its routes leave out passes, but for the
        // server's address, which only the service reaches
        await AssertEqualsWait(true, () => killSwitch.BlockCount >= 2);
        var rules = killSwitch.Rules ?? throw new InvalidOperationException("The block is not up.");
        var serverIp = token.ServerToken.HostEndPoints?.First().Address ??
                       throw new InvalidOperationException("The token has no server address.");
        Assert.IsFalse(rules.PassIpRanges.Contains(serverIp));

        await app.Disconnect();
        Assert.IsFalse(killSwitch.IsUp);
    }

    // An end that is no person's disconnect - here an adapter that stopped on its own - leaves the block up,
    // with no session, until a person disconnects.
    [TestMethod]
    public async Task An_error_stop_keeps_the_block_until_a_person_disconnects()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        var adapter = new BreakableVpnAdapter();
        await using var app = CreateApp(CreateDevice(killSwitch, adapter: adapter), killSwitchOn: true);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);

        adapter.Break();
        await app.WaitForState(AppConnectionState.None, TestCt);
        Assert.IsNotNull(app.State.LastError);
        Assert.IsTrue(killSwitch.IsUp);
        await AssertEqualsWait(true, () => app.State.IsKillSwitchBlocking);

        await app.Disconnect();
        Assert.IsFalse(killSwitch.IsUp);
        Assert.IsFalse(app.State.IsKillSwitchBlocking);
    }

    // Kept while disconnected: a person's disconnect leaves the block up, by the settings' split with no session,
    // and turning that off lifts it.
    [TestMethod]
    public async Task Lockdown_keeps_the_block_after_a_persons_disconnect()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        await using var app = CreateApp(CreateDevice(killSwitch), killSwitchOn: true, killSwitchLockdown: true);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        var sessionRules = killSwitch.Rules;

        // put up again by the settings' split, as no session's
        await app.Disconnect();
        Assert.IsTrue(killSwitch.IsUp);
        Assert.AreNotSame(sessionRules, killSwitch.Rules);
        Assert.IsTrue(app.State.IsKillSwitchBlocking);

        app.UserSettings.KillSwitchLockdown = false;
        app.SettingsService.Save();
        await AssertEqualsWait(false, () => killSwitch.IsUp);
        await AssertEqualsWait(false, () => app.State.IsKillSwitchBlocking);
    }

    // Kept while disconnected, turned on with no session: the block goes up at once; the switch turned off lifts it.
    [TestMethod]
    public async Task Lockdown_turned_on_while_disconnected_blocks_at_once()
    {
        var killSwitch = new TestVpnKillSwitch();
        await using var app = CreateApp(CreateDevice(killSwitch), killSwitchOn: true);
        Assert.IsFalse(killSwitch.IsUp);

        app.UserSettings.KillSwitchLockdown = true;
        app.SettingsService.Save();
        await AssertEqualsWait(true, () => killSwitch.IsUp);
        await AssertEqualsWait(true, () => app.State.IsKillSwitchBlocking);

        app.UserSettings.KillSwitch = false;
        app.SettingsService.Save();
        await AssertEqualsWait(false, () => killSwitch.IsUp);
        await AssertEqualsWait(false, () => app.State.IsKillSwitchBlocking);
    }

    // Kept while disconnected by default: the switch alone turned on with no session blocks at once.
    [TestMethod]
    public async Task Lockdown_is_on_by_default()
    {
        var killSwitch = new TestVpnKillSwitch();
        await using var app = TestAppHelper.CreateClientApp(device: CreateDevice(killSwitch));
        Assert.IsTrue(app.UserSettings.KillSwitchLockdown);

        app.UserSettings.KillSwitch = true;
        app.SettingsService.Save();
        await AssertEqualsWait(true, () => killSwitch.IsUp);
        await AssertEqualsWait(true, () => app.State.IsKillSwitchBlocking);
    }

    [TestMethod]
    public async Task The_switch_turned_off_lifts_a_block_an_error_stop_left()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        var adapter = new BreakableVpnAdapter();
        await using var app = CreateApp(CreateDevice(killSwitch, adapter: adapter), killSwitchOn: true);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        adapter.Break();
        await app.WaitForState(AppConnectionState.None, TestCt);
        Assert.IsTrue(killSwitch.IsUp);

        app.UserSettings.KillSwitch = false;
        app.SettingsService.Save();
        await AssertEqualsWait(false, () => killSwitch.IsUp);
    }

    // Another app's take of the tunnel is a disconnect too: this app's block goes at once, where the taker's
    // connect would replace or lift it a moment later - here never, as the stand-in taker connects nothing - and
    // the app does not say it blocks.
    [TestMethod]
    public async Task Another_apps_take_of_the_tunnel_lifts_the_block_as_a_disconnect_does()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        var tunnelLock = new TestVpnTunnelLock();
        await using var app = CreateApp(CreateDevice(killSwitch, tunnelLock), killSwitchOn: true);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);

        using var otherHold = await tunnelLock.Take(new RecordingVpnTunnelHolder(), TestCt);
        await app.WaitForState(AppConnectionState.None, TestCt);
        await AssertEqualsWait(false, () => killSwitch.IsUp);
        Assert.IsFalse(app.State.IsKillSwitchBlocking);
    }

    // Kept while disconnected, the block is left for the app that took the tunnel, whose connect replaces it with
    // no gap between the two blocks; the app does not say it blocks, as the taker's block is no longer its own.
    [TestMethod]
    public async Task Lockdown_leaves_the_block_to_the_app_that_took_the_tunnel()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        var tunnelLock = new TestVpnTunnelLock();
        await using var app = CreateApp(CreateDevice(killSwitch, tunnelLock), killSwitchOn: true,
            killSwitchLockdown: true);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);

        using var otherHold = await tunnelLock.Take(new RecordingVpnTunnelHolder(), TestCt);
        await app.WaitForState(AppConnectionState.None, TestCt);
        await AssertEqualsWait(false, () => app.State.IsKillSwitchBlocking);
        Assert.IsTrue(killSwitch.IsUp);
    }

    // The switch applies to a session at once, through its reconfigure: on puts the block up, off takes it
    // down, and a later adapter start does not put it back.
    [TestMethod]
    public async Task The_switch_applies_to_a_session_at_once()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        await using var app = CreateApp(CreateDevice(killSwitch), killSwitchOn: false);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        Assert.IsFalse(killSwitch.IsUp);

        app.UserSettings.KillSwitch = true;
        app.SettingsService.Save();
        await AssertEqualsWait(true, () => killSwitch.IsUp);

        app.UserSettings.KillSwitch = false;
        app.SettingsService.Save();
        await AssertEqualsWait(false, () => killSwitch.IsUp);
    }

    // Fail-closed: a block that cannot go up fails the connect, before any adapter comes up.
    [TestMethod]
    public async Task A_block_that_cannot_go_up_fails_the_connect()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch {
            BlockException = new UnauthorizedAccessException("The firewall refused the block.")
        };
        var device = CreateDevice(killSwitch);
        await using var app = CreateApp(device, killSwitchOn: true);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        // the state settles a moment after the connect's failure, later under load
        await Assert.ThrowsAsync<Exception>(() => app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt));
        await app.WaitForState(AppConnectionState.None, TestCt);
        Assert.IsNotNull(app.State.LastError);
        Assert.IsNull(device.VpnService?.CurrentVpnAdapter);
    }

    // The block is the tunnel's holder's: a connect without the switch removes another app's, which would
    // block this one's traffic, while this app's person's disconnect leaves it alone.
    [TestMethod]
    public async Task Another_apps_block_goes_only_with_the_tunnel()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch { IsOtherAppBlocking = true };
        await using var app = CreateApp(CreateDevice(killSwitch), killSwitchOn: false);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        await app.Disconnect();
        Assert.IsTrue(killSwitch.IsOtherAppBlocking);

        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        Assert.IsFalse(killSwitch.IsUp);
    }

    // Where the switch is a premium feature (CONNECT), a free plan connects without the block.
    [TestMethod]
    public async Task A_free_plan_connects_without_the_block_where_it_is_premium()
    {
        // no token from the server, whose own is no public one and would make the profile premium
        var accessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        accessManagerOptions.ReplyAccessKey = false;
        using var accessManager = await TestHelper.CreateAccessManager(accessManagerOptions);
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = await TestHelper.CreateAccessToken(server);
        token.IsPublic = true; // a CONNECT-style profile: premium comes from a code alone
        var killSwitch = new TestVpnKillSwitch();
        var premium = new AppPremiumOptions { Features = [AppFeature.KillSwitch], AllowImportAccessCode = true };
        await using var app = CreateApp(CreateDevice(killSwitch), killSwitchOn: true, premium);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        Assert.IsFalse(killSwitch.IsUp);
    }

    // A null-capture makes no adapter whose traffic the block could let through, so the switch refuses it.
    [TestMethod]
    public async Task A_null_capture_is_refused_with_the_switch_on()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var killSwitch = new TestVpnKillSwitch();
        await using var app = CreateApp(CreateDevice(killSwitch), killSwitchOn: true);
        app.UserSettings.DebugData1 = DebugCommands.NullCapture;
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        await Assert.ThrowsAsync<Exception>(() => app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt));
        Assert.IsFalse(killSwitch.IsUp);
    }

    // a desktop's device, which has a kill switch
    private TestDevice CreateDevice(TestVpnKillSwitch killSwitch, TestVpnTunnelLock? tunnelLock = null,
        IVpnAdapter? adapter = null)
    {
        return new TestDevice(TestAppHelper, _ => adapter ?? new TestNullVpnAdapter()) {
            KillSwitch = killSwitch,
            TunnelLock = tunnelLock
        };
    }

    private VpnHoodApp CreateApp(TestDevice device, bool killSwitchOn, AppPremiumOptions? premium = null,
        bool killSwitchLockdown = false)
    {
        var appOptions = TestAppHelper.CreateAppOptions();
        if (premium != null)
            appOptions.Premium = premium;

        var app = TestAppHelper.CreateClientApp(appOptions, device);
        app.UserSettings.KillSwitch = killSwitchOn;
        app.UserSettings.KillSwitchLockdown = killSwitchLockdown;
        app.SettingsService.Save();
        return app;
    }
}
