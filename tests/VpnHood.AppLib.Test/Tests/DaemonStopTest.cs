using System.CommandLine;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.AppLib.App;
using VpnHood.AppLib.Test.Daemon;
using VpnHood.AppUi.Hosting.Desktop.Commands;

namespace VpnHood.AppLib.Test.Tests;

// The daemon's stop, in the time a service manager gives it: a graceful close that outlives it gives way
// to the quick one, which releases the adapter before the manager ends the process.
[TestClass]
[DoNotParallelize] // the daemon holds the process's one app (VpnHoodApp.Instance)
public class DaemonStopTest : TestAppBase
{
    private VpnHoodApp CreateDaemonApp()
    {
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.WebHostFactory = new VpnHoodAppWebHostFactory();
        appOptions.WebRootZipAsset = TestAppHelper.CreateWebRootZip("web-root-test");
        appOptions.IsSingleton = true;
        return TestAppHelper.CreateClientApp(appOptions);
    }

    // the daemon command as a service manager runs it, until the stop
    private static Task<int> RunDaemon(TestDesktopPlatform platform, CancellationToken stopToken)
    {
        return DaemonCommand.Create(platform).Parse([])
            .InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null }, stopToken);
    }

    [TestMethod]
    public async Task A_graceful_close_that_outlives_the_stops_time_gives_way_to_the_quick_one()
    {
        await using var app = CreateDaemonApp();
        var daemonHost = new TestDaemonHost(app);
        daemonHost.GracefulCloseHold.IsHeld = true;
        var channel = new TestDaemonChannel();
        using var stopCts = new CancellationTokenSource();
        using var quickCloseCts = new CancellationTokenSource();
        var daemon = RunDaemon(new TestDesktopPlatform {
            Channel = channel,
            DaemonHostFactory = new TestDaemonHostFactory(daemonHost),
            QuickCloseToken = quickCloseCts.Token
        }, stopCts.Token);
        await AssertEqualsWait(true, () => channel.IsListening, "The daemon should serve.");

        // the stop: the graceful close, held, holds the daemon until the stop's time runs out
        await stopCts.CancelAsync();
        await AssertEqualsWait(1, () => daemonHost.GracefulCloseHold.HeldCount, "The graceful close should start.");
        Assert.IsFalse(daemon.IsCompleted, "The graceful close should hold the daemon.");
        await quickCloseCts.CancelAsync();

        Assert.AreEqual(0, await daemon.WaitAsync(TimeSpan.FromSeconds(10), TestCt));
        Assert.IsTrue(daemonHost.IsQuickClosed);
        Assert.IsFalse(VpnHoodApp.IsInit, "The quick close should close the app.");
        daemonHost.GracefulCloseHold.Release();
    }

    [TestMethod]
    public async Task A_stop_with_no_time_left_closes_the_app_at_once()
    {
        await using var app = CreateDaemonApp();
        var daemonHost = new TestDaemonHost(app);
        daemonHost.GracefulCloseHold.IsHeld = true;
        var channel = new TestDaemonChannel();
        using var stopCts = new CancellationTokenSource();
        using var quickCloseCts = new CancellationTokenSource();
        var daemon = RunDaemon(new TestDesktopPlatform {
            Channel = channel,
            DaemonHostFactory = new TestDaemonHostFactory(daemonHost),
            QuickCloseToken = quickCloseCts.Token
        }, stopCts.Token);
        await AssertEqualsWait(true, () => channel.IsListening, "The daemon should serve.");

        // a shutdown: its time is gone as the stop comes
        await quickCloseCts.CancelAsync();
        await stopCts.CancelAsync();

        Assert.AreEqual(0, await daemon.WaitAsync(TimeSpan.FromSeconds(10), TestCt));
        Assert.AreEqual(0, daemonHost.GracefulCloseHold.HeldCount, "No graceful close should start.");
        Assert.IsTrue(daemonHost.IsQuickClosed);
        Assert.IsFalse(VpnHoodApp.IsInit, "The quick close should close the app.");
    }
}
