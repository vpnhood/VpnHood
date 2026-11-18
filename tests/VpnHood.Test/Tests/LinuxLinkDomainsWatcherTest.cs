using System.Diagnostics;
using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.Test.Tests;

// The watch that tells the Linux adapter when an interface's DNS settings may have changed, on folders of the test's
// own: on Linux the folders are systemd's. That systemd-resolved writes there as a link's domains change is checked
// on Linux by hand, as is a watch the system refuses.
[TestClass]
public class LinuxLinkDomainsWatcherTest : TestBase
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(100);

    private string CreateFolder()
    {
        var folder = Path.Combine(TestHelper.WorkingPath, $"link-watch-{Guid.NewGuid():N}", "resolve");
        Directory.CreateDirectory(folder);
        return folder;
    }

    [TestMethod]
    public async Task A_change_in_a_watched_folder_is_told()
    {
        var folder = CreateFolder();
        var lookCount = 0;
        using var watcher = new LinuxLinkDomainsWatcher([folder], SettleTime, () => Interlocked.Increment(ref lookCount));

        // as systemd-resolved rewrites a file: a new one, moved over the old
        var tempPath = Path.Combine(folder, ".#resolv.conf");
        await File.WriteAllTextAsync(tempPath, "search lan\n", TestCt);
        File.Move(tempPath, Path.Combine(folder, "resolv.conf"), overwrite: true);

        await AssertEqualsWait(true, () => Volatile.Read(ref lookCount) > 0);
    }

    [TestMethod]
    public async Task A_change_in_a_folder_made_after_the_watch_began_is_told()
    {
        // systemd-resolved makes its netif folder with the first link whose settings it keeps
        var folder = CreateFolder();
        var lookCount = 0;
        using var watcher = new LinuxLinkDomainsWatcher([folder], SettleTime, () => Interlocked.Increment(ref lookCount));

        var netifFolder = Path.Combine(folder, "netif");
        Directory.CreateDirectory(netifFolder);
        await AssertEqualsWait(true, () => Volatile.Read(ref lookCount) > 0);

        // once that look is over, a link's file in the new folder brings the next
        await Task.Delay(SettleTime * 3, TestCt);
        var lookCountBefore = Volatile.Read(ref lookCount);
        await File.WriteAllTextAsync(Path.Combine(netifFolder, "2"), "DOMAINS=lan\n", TestCt);

        await AssertEqualsWait(true, () => Volatile.Read(ref lookCount) > lookCountBefore);
    }

    [TestMethod]
    public async Task A_burst_of_changes_is_told_fewer_times_than_it_has_changes()
    {
        // a manager sets a link's servers, its domains and the rest one after another. The settle time is long, so
        // the burst falls in one or two of them however slow the machine.
        const int changeCount = 20;
        var settleTime = TimeSpan.FromSeconds(1);
        var folder = CreateFolder();
        var lookCount = 0;
        using var watcher = new LinuxLinkDomainsWatcher([folder], settleTime, () => Interlocked.Increment(ref lookCount));

        for (var i = 0; i < changeCount; i++)
            await File.WriteAllTextAsync(Path.Combine(folder, "2"), $"DOMAINS=lan{i}\n", TestCt);

        await AssertEqualsWait(true, () => Volatile.Read(ref lookCount) > 0);
        await Task.Delay(settleTime * 2, TestCt);
        Assert.IsLessThan(changeCount, Volatile.Read(ref lookCount));
    }

    [TestMethod]
    public async Task A_steady_run_of_changes_does_not_put_the_look_off()
    {
        // each change far sooner after the last than the settle time: put off by each, the look would never come
        var settleTime = TimeSpan.FromMilliseconds(500);
        var folder = CreateFolder();
        var lookCount = 0;
        using var watcher = new LinuxLinkDomainsWatcher([folder], settleTime, () => Interlocked.Increment(ref lookCount));
        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var writeTask = WriteSteadily(Path.Combine(folder, "2"), TimeSpan.FromMilliseconds(20), writeCts.Token);
        try {
            await AssertEqualsWait(true, () => Volatile.Read(ref lookCount) > 0);
        }
        finally {
            await writeCts.CancelAsync();
            await writeTask;
        }
    }

    [TestMethod]
    public async Task Nothing_is_told_once_the_watch_has_ended()
    {
        var folder = CreateFolder();
        var lookCount = 0;
        var watcher = new LinuxLinkDomainsWatcher([folder], SettleTime, () => Interlocked.Increment(ref lookCount));
        watcher.Dispose();

        await File.WriteAllTextAsync(Path.Combine(folder, "2"), "DOMAINS=lan\n", TestCt);
        await Task.Delay(SettleTime * 5, TestCt);

        Assert.AreEqual(0, Volatile.Read(ref lookCount));
    }

    [TestMethod]
    public async Task A_change_that_waits_for_its_look_as_the_watch_ends_is_not_told()
    {
        // A stop that gives the domains back ends the watch while a change waits for its look. The look comes a
        // settle time after the change at the earliest, so a watch that ended sooner after the write ended before any
        // look could come; a test held up past that proves nothing, and says so.
        var settleTime = TimeSpan.FromSeconds(2);
        var folder = CreateFolder();
        var lookCount = 0;
        var watcher = new LinuxLinkDomainsWatcher([folder], settleTime, () => Interlocked.Increment(ref lookCount));
        var sinceWrite = Stopwatch.StartNew();
        await File.WriteAllTextAsync(Path.Combine(folder, "2"), "DOMAINS=lan\n", TestCt);
        await Task.Delay(SettleTime, TestCt); // the change's event comes in
        watcher.Dispose();
        var endedAfter = sinceWrite.Elapsed;

        await Task.Delay(settleTime + SettleTime * 5, TestCt);

        if (endedAfter >= settleTime)
            Assert.Inconclusive($"The test was held up {endedAfter} before the watch ended, past the settle time.");

        Assert.AreEqual(0, Volatile.Read(ref lookCount));
    }

    [TestMethod]
    public async Task A_folder_that_is_not_there_is_passed_over()
    {
        // systemd-networkd's folder is missing where systemd-networkd has never run
        var folder = CreateFolder();
        var missingFolder = Path.Combine(TestHelper.WorkingPath, $"link-watch-{Guid.NewGuid():N}", "links");
        var lookCount = 0;
        using var watcher = new LinuxLinkDomainsWatcher([missingFolder, folder], SettleTime,
            () => Interlocked.Increment(ref lookCount));

        await File.WriteAllTextAsync(Path.Combine(folder, "2"), "DOMAINS=lan\n", TestCt);

        await AssertEqualsWait(true, () => Volatile.Read(ref lookCount) > 0);
    }

    [TestMethod]
    public void A_watch_with_no_folder_to_watch_fails()
    {
        var missingFolder = Path.Combine(TestHelper.WorkingPath, $"link-watch-{Guid.NewGuid():N}", "resolve");

        Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
            new LinuxLinkDomainsWatcher([missingFolder], SettleTime, () => { }).Dispose());
    }

    private static async Task WriteSteadily(string path, TimeSpan interval, CancellationToken cancellationToken)
    {
        for (var i = 0; !cancellationToken.IsCancellationRequested; i++) {
            await File.WriteAllTextAsync(path, $"DOMAINS=lan{i}\n", CancellationToken.None);
            await Task.Delay(interval, CancellationToken.None);
        }
    }
}
