using System.Diagnostics;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App.Utils;
using VpnHood.Core.Client.Devices.Linux;
using VpnHood.Core.Client.Devices.Windows;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Test.Device;

namespace VpnHood.AppLib.Test.Tests;

// The machine's one tunnel (IVpnTunnelLock): the VPN service takes it before its adapter comes up and
// lets it go once the adapter is gone; another app's take ends this app's connection, as a VPN revoked
// on Android, and waits only for its adapter to go. The OS locks, Windows' and Linux's, need an
// administrator or root.
[TestClass]
public class VpnTunnelLockTest : TestAppBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task Another_app_takes_the_tunnel_and_this_one_takes_it_back()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var tunnelLock = new TestVpnTunnelLock();
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter()) { TunnelLock = tunnelLock };
        await using var app = TestAppHelper.CreateClientApp(device: device);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestContext.CancellationToken);
        await app.WaitForState(AppConnectionState.Connected);
        Assert.IsTrue(tunnelLock.IsHeld);

        // another app takes it: this one's adapter is gone by the time the take returns, and the app ends
        // with the revoked error
        var otherApp = new RecordingVpnTunnelHolder();
        using var otherHold = await tunnelLock.Take(otherApp, TestContext.CancellationToken)
            .WaitAsync(Timeout, TestContext.CancellationToken);
        Assert.IsNull(device.VpnService?.CurrentVpnAdapter);
        await app.WaitForState(AppConnectionState.None);
        Assert.AreEqual(nameof(VpnServiceRevokedException), app.State.LastError?.TypeName);

        // a person connects again: the other app is asked, and this one connects with nothing more of it
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestContext.CancellationToken);
        await app.WaitForState(AppConnectionState.Connected);
        Assert.IsTrue(otherApp.Asked.IsCompleted);
        Assert.IsTrue(tunnelLock.IsHeld);

        // a person's disconnect lets it go unasked
        await app.Disconnect();
        await app.WaitForState(AppConnectionState.None);
        await AssertEqualsWait(false, () => tunnelLock.IsHeld);
    }

    // A null-capture brings no adapter up, so it takes no tunnel: a run as the person ("dev") connects
    // with it, and could not reach the OS's lock.
    [TestMethod]
    public async Task Null_capture_takes_no_tunnel()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var tunnelLock = new TestVpnTunnelLock();
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter()) { TunnelLock = tunnelLock };
        await using var app = TestAppHelper.CreateClientApp(device: device);
        app.UserSettings.DebugData1 = DebugCommands.NullCapture;
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestContext.CancellationToken);
        await app.WaitForState(AppConnectionState.Connected);
        Assert.IsFalse(tunnelLock.IsHeld);
    }

    // The tunnel changes hands once its holder has dropped its adapter, never before, and with nothing more
    // of that app: its hold ends by the ask.
    [TestMethod]
    [DoNotParallelize] // the machine's one tunnel
    public async Task Os_lock_hands_the_tunnel_over_once_its_holder_has_let_go()
    {
        var tunnelLock = CreateOsTunnelLock();
        var firstHolder = new RecordingVpnTunnelHolder { LetGoTime = TimeSpan.FromSeconds(1) };
        using var firstHold = await tunnelLock.Take(firstHolder, TestContext.CancellationToken)
            .WaitAsync(Timeout, TestContext.CancellationToken);

        var secondHolder = new RecordingVpnTunnelHolder();
        using var secondHold = await tunnelLock.Take(secondHolder, TestContext.CancellationToken)
            .WaitAsync(Timeout, TestContext.CancellationToken);
        Assert.IsTrue(firstHolder.HasLetGo, "The tunnel must not change hands before its holder has let go.");
        Assert.IsFalse(secondHolder.Asked.IsCompleted);
    }

    [TestMethod]
    [DoNotParallelize] // the machine's one tunnel
    public async Task Os_lock_let_go_unasked_is_taken_without_asking()
    {
        var tunnelLock = CreateOsTunnelLock();
        var firstHolder = new RecordingVpnTunnelHolder();
        var firstHold = await tunnelLock.Take(firstHolder, TestContext.CancellationToken)
            .WaitAsync(Timeout, TestContext.CancellationToken);
        firstHold.Dispose();

        var secondHolder = new RecordingVpnTunnelHolder();
        using var secondHold = await tunnelLock.Take(secondHolder, TestContext.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken);
        Assert.IsFalse(firstHolder.Asked.IsCompleted);

        // and the holder that came after is asked by the next take, not left an ask of before
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.CancellationToken);
        Assert.IsFalse(secondHolder.Asked.IsCompleted);
    }

    // A take that waits for a holder slow to let go - a frozen app, say - ends on its cancellation.
    [TestMethod]
    [DoNotParallelize] // the machine's one tunnel
    public async Task Os_lock_take_ends_on_cancellation()
    {
        var tunnelLock = CreateOsTunnelLock();
        using var firstHold = await tunnelLock.Take(new RecordingVpnTunnelHolder { LetGoTime = TimeSpan.FromSeconds(3) },
            TestContext.CancellationToken).WaitAsync(Timeout, TestContext.CancellationToken);

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            tunnelLock.Take(new RecordingVpnTunnelHolder(), cancellationTokenSource.Token));
    }

    private static IVpnTunnelLock CreateOsTunnelLock()
    {
        if (!Environment.IsPrivilegedProcess)
            Assert.Inconclusive("Run elevated, or as root: only they reach the tunnel's lock.");

        if (OperatingSystem.IsWindows())
            return new WindowsVpnTunnelLock();

        if (OperatingSystem.IsLinux())
            return new LinuxVpnTunnelLock();

        Assert.Inconclusive("Windows and Linux only: the other platforms hand the tunnel over themselves.");
        throw new UnreachableException();
    }
}
