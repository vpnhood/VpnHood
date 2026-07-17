using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class FastDateTimeTest
{
    [TestMethod]
    public void Now_should_be_local_kind_and_within_precision()
    {
        var before = DateTime.Now;
        var fast = FastDateTime.Now;
        var after = DateTime.Now;

        Assert.AreEqual(DateTimeKind.Local, fast.Kind);
        Assert.IsGreaterThanOrEqualTo(before - FastDateTime.Precision - TimeSpan.FromSeconds(5), fast,
            $"FastDateTime.Now is too old. fast: {fast:O}, before: {before:O}");
        Assert.IsLessThanOrEqualTo(after + TimeSpan.FromSeconds(1), fast,
            $"FastDateTime.Now is in the future. fast: {fast:O}, after: {after:O}");
    }

    [TestMethod]
    public void UtcNow_should_be_utc_kind_and_within_precision()
    {
        var before = DateTime.UtcNow;
        var fast = FastDateTime.UtcNow;
        var after = DateTime.UtcNow;

        Assert.AreEqual(DateTimeKind.Utc, fast.Kind);
        Assert.IsGreaterThanOrEqualTo(before - FastDateTime.Precision - TimeSpan.FromSeconds(5), fast,
            $"FastDateTime.UtcNow is too old. fast: {fast:O}, before: {before:O}");
        Assert.IsLessThanOrEqualTo(after + TimeSpan.FromSeconds(1), fast,
            $"FastDateTime.UtcNow is in the future. fast: {fast:O}, after: {after:O}");
    }

    [TestMethod]
    public void Parallel_reads_should_track_the_system_clock()
    {
        // FastDateTime deliberately follows system clock steps in both directions, so asserting
        // monotonicity would be flaky. Comparing against DateTime.UtcNow tracks OS clock adjustments
        // (both clocks move together) and still catches stale or torn samples. A clock step can still
        // straddle one sample pair, so only consecutive violations fail; a real defect persists
        var slack = TimeSpan.FromSeconds(5);
        var tasks = new Task[8];
        for (var i = 0; i < tasks.Length; i++)
            tasks[i] = Task.Run(() => {
                var violations = 0;
                for (var j = 0; j < 200_000; j++) {
                    var diff = DateTime.UtcNow - FastDateTime.UtcNow;
                    if (diff > FastDateTime.Precision + slack || diff < -slack) {
                        if (++violations >= 2)
                            Assert.Fail($"FastDateTime.UtcNow drifted from the system clock. diff: {diff}");
                    }
                    else {
                        violations = 0;
                    }
                }
            });

        Task.WaitAll(tasks);
    }

    [TestMethod]
    [DoNotParallelize] // Precision is process-global; do not degrade it under parallel tests
    public void Precision_should_control_the_refresh_rate()
    {
        var oldPrecision = FastDateTime.Precision;
        try {
            FastDateTime.Precision = TimeSpan.FromHours(1);
            var first = FastDateTime.Now;
            Thread.Sleep(50);
            Assert.AreEqual(first, FastDateTime.Now, "within the precision window the cached value must be returned");

            FastDateTime.Precision = TimeSpan.FromMilliseconds(1);
            Thread.Sleep(100); // TickCount64 granularity is ~16ms; give the gate room to reopen
            var refreshed = FastDateTime.Now;
            Assert.AreNotEqual(first, refreshed, "after the precision window the value must refresh");
            Assert.IsLessThan(TimeSpan.FromSeconds(5), (DateTime.Now - refreshed).Duration());
        }
        finally {
            FastDateTime.Precision = oldPrecision;
        }
    }
}
