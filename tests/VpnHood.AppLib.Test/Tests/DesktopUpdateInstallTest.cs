using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppLib.Test.Providers;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Updaters;

namespace VpnHood.AppLib.Test.Tests;

// The desktop service's install of a downloaded update (IdleUpdateInstaller, DesktopAppUpdaterProvider): it waits for
// the VPN to stay idle, so a server switch or a quick reconnect never meets it, a connect or a stop before the
// installer starts puts it off, and while the installer runs the app is Updating, which refuses connects.
[TestClass]
public class DesktopUpdateInstallTest : TestAppBase
{
    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(1);

    [TestMethod]
    public async Task An_install_is_updating_and_refuses_a_connect()
    {
        var updater = CreateUpdater();
        await using var app = CreateApp(updater);
        await AddDownload(updater);

        // the installer under way, on a download the updater left: the notice and status say installing too
        var install = updater.InstallDownloadedUpdate(TestCt);
        Assert.AreEqual(AppConnectionState.Updating, app.State.ConnectionState);
        var release = new AppRelease {
            Version = new Version(2, 0, 0), PackageUrl = new Uri("https://localhost/setup.exe")
        };
        Assert.AreEqual(AppUpdateStage.Installing, updater.GetState(release).Stage);
        Assert.IsFalse(app.State.CanConnect);
        Assert.IsFalse(app.State.CanDisconnect);
        await Assert.ThrowsExactlyAsync<AppUpdatingException>(() =>
            app.Connect(uiContext: null, connectOptions: null, TestCt));

        // one that ends without installing leaves the app idle again, and the refusal no last error
        updater.InstallerEnd.SetResult("The installed version is up to date.");
        await install;
        Assert.AreEqual(AppConnectionState.None, app.State.ConnectionState);
        Assert.IsTrue(app.State.CanConnect);
        Assert.IsNull(app.State.LastError);
    }

    [TestMethod]
    public async Task A_server_switch_and_a_quick_reconnect_install_nothing()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var updater = CreateUpdater();
        updater.InstallerEnd.SetResult("The installed version is up to date.");
        await using var app = CreateApp(updater);
        using var idleUpdateInstaller = new IdleUpdateInstaller(app, updater, IdleWait);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        await AddDownload(updater);

        // a switch disconnects first, then connects at once
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        await Task.Delay(IdleWait * 2, TestCt);
        Assert.AreEqual(0, updater.InstallerRuns, "a server switch");

        // a disconnect, and a connect again at once
        await app.Disconnect();
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        await Task.Delay(IdleWait * 2, TestCt);
        Assert.AreEqual(0, updater.InstallerRuns, "a quick reconnect");

        // a disconnect that stays installs once the wait is over, one try for this end
        await app.Disconnect();
        await AssertEqualsWait(1, () => updater.InstallerRuns);
        await Task.Delay(IdleWait * 2, TestCt);
        Assert.AreEqual(1, updater.InstallerRuns, "one try per session's end");
        Assert.AreEqual(AppConnectionState.None, app.State.ConnectionState);
    }

    [TestMethod]
    public async Task A_connect_before_the_installer_starts_puts_the_install_off()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var updater = CreateUpdater();
        updater.InstallerEnd.SetResult("The installed version is up to date.");
        updater.CheckEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = CreateApp(updater);
        using var idleUpdateInstaller = new IdleUpdateInstaller(app, updater, IdleWait);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        await AddDownload(updater);

        // the wait is over and the switch's check under way when a person connects
        await app.Disconnect();
        await updater.CheckStarted.Task.WaitAsync(IdleWait * 10, TestCt);
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        await AssertEqualsWait(true, () => updater.CheckToken.IsCancellationRequested);
        updater.CheckEnd.SetResult();
        await Task.Delay(IdleWait * 2, TestCt);
        Assert.AreEqual(0, updater.InstallerRuns, "a connect during the check");

        // put off to the next session's end
        await app.Disconnect();
        await AssertEqualsWait(1, () => updater.InstallerRuns);
    }

    [TestMethod]
    public async Task A_stop_before_the_installer_starts_installs_nothing()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var updater = CreateUpdater();
        updater.InstallerEnd.SetResult("The installed version is up to date.");
        updater.CheckEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = CreateApp(updater);
        using var idleUpdateInstaller = new IdleUpdateInstaller(app, updater, IdleWait);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        await AddDownload(updater);

        // the wait is over and the switch's check under way when the service stops
        await app.Disconnect();
        await updater.CheckStarted.Task.WaitAsync(IdleWait * 10, TestCt);
        idleUpdateInstaller.Dispose();
        Assert.IsTrue(updater.CheckToken.IsCancellationRequested);
        updater.CheckEnd.SetResult();
        await Task.Delay(IdleWait * 2, TestCt);
        Assert.AreEqual(0, updater.InstallerRuns);
    }

    // each test's own install folder
    private TestDesktopUpdaterProvider CreateUpdater()
    {
        var folderPath = Directory.CreateDirectory(Path.Combine(TestHelper.WorkingPath, $"desktop-{Guid.NewGuid():N}"))
            .FullName;
        return new TestDesktopUpdaterProvider(folderPath);
    }

    private VpnHoodApp CreateApp(TestDesktopUpdaterProvider updater)
    {
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdaterOptions = new AppUpdaterOptions { UpdaterProvider = updater };
        return TestAppHelper.CreateClientApp(appOptions);
    }

    // a release newer than the double's 1.0.0, downloaded whole
    private Task AddDownload(TestDesktopUpdaterProvider updater)
    {
        Directory.CreateDirectory(updater.UpdatesPath);
        return File.WriteAllTextAsync(Path.Combine(updater.UpdatesPath, "2.0.0-setup.exe"), "setup", TestCt);
    }
}
