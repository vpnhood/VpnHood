using VpnHood.AppLib.Test.Providers;

namespace VpnHood.AppLib.Test.Tests;

// The service's retry at a connect (DesktopAppUpdaterProvider.RetryMissedUpdate): the updater runs again only after a
// run of its own that could not reach the server, and only while automatic updates are on.
[TestClass]
public class MissedUpdateRetryTest : TestAppBase
{
    [TestMethod]
    public async Task A_run_that_missed_the_server_is_made_again()
    {
        var updater = CreateUpdater();
        await File.WriteAllTextAsync(updater.MissedPath, "", TestCt);

        await updater.RetryMissedUpdate(TestCt);
        Assert.AreEqual(1, updater.RunCalls);
    }

    [TestMethod]
    public async Task No_run_is_made_without_a_miss()
    {
        var updater = CreateUpdater();

        await updater.RetryMissedUpdate(TestCt);
        Assert.AreEqual(0, updater.RunCalls);
    }

    [TestMethod]
    public async Task No_run_is_made_while_automatic_updates_are_off()
    {
        var updater = CreateUpdater();
        await File.WriteAllTextAsync(updater.MissedPath, "", TestCt);
        updater.IsOn = false;

        await updater.RetryMissedUpdate(TestCt);
        Assert.AreEqual(0, updater.RunCalls);
    }

    // a connect goes on whatever the updater does: a run that could not start is logged
    [TestMethod]
    public async Task An_updater_that_cannot_start_fails_no_connect()
    {
        var updater = CreateUpdater();
        await File.WriteAllTextAsync(updater.MissedPath, "", TestCt);
        updater.RunError = new InvalidOperationException("No systemctl.");

        await updater.RetryMissedUpdate(TestCt);
        Assert.AreEqual(1, updater.RunCalls);
    }

    // each test's own install folder
    private TestDesktopUpdaterProvider CreateUpdater()
    {
        var folderPath = Directory.CreateDirectory(Path.Combine(TestHelper.WorkingPath, $"desktop-{Guid.NewGuid():N}"))
            .FullName;
        return new TestDesktopUpdaterProvider(folderPath);
    }
}
