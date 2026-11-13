using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class UtilTest : TestBase
{
    // counts its reports and fails each one
    private sealed class FailingEventReporter() : EventReporter("UnitTest", period: TimeSpan.FromMilliseconds(100))
    {
        private int _reportCount;
        public int ReportCount => Volatile.Read(ref _reportCount);

        protected override void Report()
        {
            Interlocked.Increment(ref _reportCount);
            throw new InvalidOperationException("Test: the report failed.");
        }
    }

    private class TestEventReporter(string message, TimeSpan period)
        : EventReporter(message, period: period)
    {
        public int ReportedCount { get; private set; }

        protected override void Report()
        {
            base.Report();
            ReportedCount++;
        }
    }

    [TestMethod]
    public void A_command_line_keeps_a_quoted_part_with_what_stands_beside_it()
    {
        CollectionAssert.AreEqual(
            new[] { "a", "b cd", "", "ef g", "h i" },
            VhUtils.ParseArguments("a  \"b c\"d \"\" e\"f g\"\t\"h i").ToArray());
        Assert.IsEmpty(VhUtils.ParseArguments("   ").ToArray());
    }

    [TestMethod]
    public void A_client_id_is_encrypted_with_any_aes_key_and_at_any_length()
    {
        var clientId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        var key = Enumerable.Range(1, 16).Select(x => (byte)x).ToArray();

        // a GUID under a 128-bit key: the bytes this always gave, which the servers in use check
        using var aes = Aes.Create();
        aes.Key = key;
        var expected = aes.EncryptCbc(clientId.ToByteArray(), new byte[16], PaddingMode.None);
        CollectionAssert.AreEqual(expected, VhUtils.EncryptClientId(clientId.ToString(), key));

        // a longer key, and an ID that is no GUID nor a whole number of blocks
        Assert.HasCount(16, VhUtils.EncryptClientId(clientId.ToString(), new byte[32]));
        Assert.HasCount(16, VhUtils.EncryptClientId("client-1", key));
        CollectionAssert.AreEqual(
            VhUtils.EncryptClientId("client-1", key), VhUtils.EncryptClientId("client-1", key));
    }

    [TestMethod]
    public void A_patch_equals_another_of_its_value()
    {
        var patch = new Patch<string?>("a");

        // ReSharper disable once EqualExpressionComparison
        Assert.IsTrue(patch.Equals(patch));
        Assert.IsTrue(patch.Equals(new Patch<string?>("a")));
        Assert.IsFalse(patch.Equals(new Patch<string?>("b")));
        Assert.IsTrue(patch.Equals("a"));
        Assert.IsTrue(new Patch<string?>(null).Equals(new Patch<string?>(null)));
    }

    [TestMethod]
    public async Task An_event_reporter_whose_last_report_fails_still_ends_its_job()
    {
        var reporter = new FailingEventReporter();

        // an event whose report fails, so it is still to report at the disposal; at the Debug level the raise
        // reports at once, and fails itself
        try {
            reporter.Raise();
        }
        catch (InvalidOperationException) {
            // the report's own failure
        }

        // the last report fails: the reporter's job must not live on, reporting again and again
        try {
            reporter.Dispose();
        }
        catch (InvalidOperationException) {
            // the report's own failure, where the disposal lets it out
        }

        var reportCount = reporter.ReportCount;
        await Task.Delay(TimeSpan.FromSeconds(5), TestCt); // more than two of the job runner's ticks
        Assert.AreEqual(reportCount, reporter.ReportCount);
    }

    // A command that ends at once but leaves a child holding its output: the read of that output must not outlast
    // the timeout
    [TestMethod]
    public void A_command_whose_child_keeps_its_output_open_ends_at_the_timeout()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.ThrowsExactly<TimeoutException>(() => {
            if (OperatingSystem.IsWindows())
                OsUtils.ExecuteCommand("cmd.exe", "/c start /b ping -n 30 127.0.0.1", TimeSpan.FromSeconds(2));
            else
                OsUtils.ExecuteCommand("/bin/sh", "-c \"sleep 30 &\"", TimeSpan.FromSeconds(2));
        });
        Assert.IsLessThan(TimeSpan.FromSeconds(15), stopwatch.Elapsed);
    }

    [TestMethod]
    public void Common_files_take_single_names_only()
    {
        if (!OperatingSystem.IsWindows()) {
            Assert.Inconclusive("Common Files is a folder of Windows.");
            return;
        }

        using var content = new MemoryStream([1, 2, 3]);
        var files = new Dictionary<string, Stream> { { "a.dll", content } };

        // a rooted name first: where it is taken, the folder lands in the test's own folder, not under Program Files
        var rootedName = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        Assert.ThrowsExactly<ArgumentException>(() => WindowsCommonFiles.Install(rootedName, files));
        Assert.ThrowsExactly<ArgumentException>(() => WindowsCommonFiles.Install("..\\name", files));
        Assert.ThrowsExactly<ArgumentException>(() => WindowsCommonFiles.Install("", files));
        Assert.ThrowsExactly<ArgumentException>(() => WindowsCommonFiles.Install("name",
            new Dictionary<string, Stream> { { "..\\a.dll", content } }));
        Assert.ThrowsExactly<ArgumentException>(() => WindowsCommonFiles.Install("name",
            new Dictionary<string, Stream> { { Path.Combine(TestHelper.WorkingPath, "a.dll"), content } }));
    }

    [TestMethod]
    public void A_rate_in_gigabits_shows_its_fraction()
    {
        // 1.5 Gbps and 2 Gbps, in bytes per second
        Assert.AreEqual(1.5.ToString("0.# ") + "Gbps", VhUtils.FormatBits(201_326_592));
        Assert.AreEqual("2 Gbps", VhUtils.FormatBits(268_435_456));
        Assert.AreEqual("8 Mbps", VhUtils.FormatBits(1_048_576));
    }

    [TestMethod]
    [DoNotParallelize] // timing-sensitive; CPU contention shifts the report period
    public async Task EventReportCounter()
    {
        VhLogger.MinLogLevel = LogLevel.Information;
        using var reportCounter = new TestEventReporter("UnitTest", period: TimeSpan.FromMilliseconds(1000));

        Assert.AreEqual(0, reportCounter.ReportedCount);
        reportCounter.Raise();
        Assert.AreEqual(1, reportCounter.TotalEventCount);
        await VhTestUtil.AssertEqualsWait(1, () => reportCounter.ReportedCount);

        reportCounter.Raise();
        reportCounter.Raise();
        reportCounter.Raise();
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        Assert.AreEqual(1, reportCounter.ReportedCount);

        // wait for the next report
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        await VhTestUtil.AssertEqualsWait(2, () => reportCounter.ReportedCount);

        reportCounter.Raise(); // immediate
        Assert.AreEqual(5, reportCounter.TotalEventCount);
        Assert.AreEqual(2, reportCounter.ReportedCount);
        await VhTestUtil.AssertEqualsWait(3, () => reportCounter.ReportedCount);
    }

    // a read that never ends outlasts the wait's timeout, which is checked between reads: the caller's token ends it
    [TestMethod]
    public async Task AssertEqualsWait_ends_a_read_that_never_ends_with_the_token()
    {
        var never = new TaskCompletionSource<int>().Task;
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        cancellationTokenSource.CancelAfter(200);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
                VhTestUtil.AssertEqualsWait(1, never, cancellationToken: cancellationTokenSource.Token))
            .WaitAsync(TimeSpan.FromSeconds(10), TestCt);
    }
}