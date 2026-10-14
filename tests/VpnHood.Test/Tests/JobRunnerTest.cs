using System.Diagnostics;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class JobRunnerTest
{
    // due at once: it runs at its runner's first tick
    private static Job CreateJob(JobRunner jobRunner)
    {
        return new Job(_ => default, new JobOptions { DueTime = TimeSpan.Zero }, jobRunner);
    }

    [TestMethod]
    public async Task Runner_asleep_on_its_interval_takes_a_shorter_one_at_once()
    {
        var jobRunner = new JobRunner(TimeSpan.FromMinutes(10));
        using var job = CreateJob(jobRunner);

        // the runner is asleep on its ten minutes by now
        await Task.Delay(500);
        jobRunner.Interval = TimeSpan.FromMilliseconds(100);
        await VhTestUtil.AssertEqualsWait(true, () => job.SucceededCount > 0,
            "The runner kept sleeping on its old interval");
    }

    // A set restarts the timer's countdown, so the same interval set again must change nothing, or the TestHelper of
    // each test starting would keep putting off the tick
    [TestMethod]
    public async Task Same_interval_set_again_and_again_does_not_put_off_the_tick()
    {
        var interval = TimeSpan.FromMilliseconds(500);
        var jobRunner = new JobRunner(interval);
        using var job = CreateJob(jobRunner);

        // set closer together than the interval, until the job runs
        var stopwatch = Stopwatch.StartNew();
        while (job.SucceededCount == 0) {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(3))
                Assert.Fail("The runner did not tick while its interval was set again and again.");

            jobRunner.Interval = interval;
            await Task.Delay(50);
        }
    }
}
