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

    [TestMethod]
    public async Task A_new_parallelism_is_not_lifted_by_the_runs_of_the_old_one()
    {
        var jobRunner = new JobRunner(TimeSpan.FromMilliseconds(20)) { MaxDegreeOfParallelism = 1 };
        var options = new JobOptions { DueTime = TimeSpan.Zero, Interval = TimeSpan.FromHours(1) };
        var running = 0;
        var maxRunning = 0;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var first = new Job(_ => Run(firstEntered, firstRelease), options, jobRunner);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // a new parallelism of one while the first job runs under the old one, whose slot it then gives back
        jobRunner.MaxDegreeOfParallelism = 1;
        firstRelease.SetResult();
        await VhTestUtil.AssertEqualsWait(1L, () => first.SucceededCount);

        // two jobs due at once and one slot: one at a time
        using var second = new Job(_ => Run(laterEntered, laterRelease), options, jobRunner);
        using var third = new Job(_ => Run(laterEntered, laterRelease), options, jobRunner);
        await laterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300); // the other one would be in by now
        Assert.AreEqual(1, Volatile.Read(ref maxRunning));
        laterRelease.SetResult();
        return;

        async ValueTask Run(TaskCompletionSource entered, TaskCompletionSource release)
        {
            var now = Interlocked.Increment(ref running);
            int max;
            while (now > (max = Volatile.Read(ref maxRunning)))
                Interlocked.CompareExchange(ref maxRunning, now, max);

            entered.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref running);
        }
    }

    [TestMethod]
    public async Task A_job_stopped_while_it_waits_for_a_slot_does_not_run()
    {
        var jobRunner = new JobRunner(TimeSpan.FromMilliseconds(20)) { MaxDegreeOfParallelism = 1 };
        var options = new JobOptions { DueTime = TimeSpan.Zero, Interval = TimeSpan.FromHours(1) };
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRuns = 0;

        // both are due: the first takes the only slot, and the second waits for it
        using var first = new Job(async _ => {
            firstEntered.TrySetResult();
            await firstRelease.Task;
        }, options, jobRunner);
        using var second = new Job(_ => {
            Interlocked.Increment(ref secondRuns);
            return default;
        }, options, jobRunner);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200); // the runner has found the second one due by now, and waits for the slot

        second.Stop();
        firstRelease.SetResult();
        await VhTestUtil.AssertEqualsWait(1L, () => first.SucceededCount);
        await Task.Delay(300); // its turn has come by now
        Assert.AreEqual(0, Volatile.Read(ref secondRuns));
    }

    [TestMethod]
    public async Task A_job_that_fails_its_last_retry_throws_its_own_exception_though_it_is_not_started()
    {
        var jobRunner = new JobRunner(TimeSpan.FromMinutes(10));
        using var job = new Job(_ => throw new IOException("Test: the job failed."),
            new JobOptions { MaxRetry = 0, AutoStart = false }, jobRunner);

        await Assert.ThrowsExactlyAsync<IOException>(() => job.RunNow());
        Assert.IsFalse(job.IsStarted);
        Assert.AreEqual(1, job.FailedCount);
    }
}
