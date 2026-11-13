using VpnHood.Net.Toolkit.Monitoring;

namespace VpnHood.Test.Tests;

[TestClass]
public class ProgressMonitorTest
{
    [TestMethod]
    public void A_run_of_no_tasks_is_complete()
    {
        var monitor = new ProgressMonitor(totalTaskCount: 0, taskTimeout: TimeSpan.FromSeconds(10));
        Assert.AreEqual(100, monitor.Progress.Percentage);

        monitor.Finish();
        Assert.AreEqual(100, monitor.Progress.Percentage);
        Assert.AreEqual(0, monitor.Progress.Total);
    }

    [TestMethod]
    public void Without_a_task_timeout_the_percentage_follows_the_completed_tasks()
    {
        var monitor = new ProgressMonitor(totalTaskCount: 4, taskTimeout: TimeSpan.Zero, maxDegreeOfParallelism: 2);
        Assert.AreEqual(0, monitor.Progress.Percentage);

        monitor.IncrementCompleted();
        Assert.AreEqual(25, monitor.Progress.Percentage);

        monitor.Finish();
        Assert.AreEqual(100, monitor.Progress.Percentage);
    }

    [TestMethod]
    public void A_parallelism_below_one_or_a_negative_count_is_refused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new ProgressMonitor(totalTaskCount: 3, taskTimeout: TimeSpan.FromSeconds(1), maxDegreeOfParallelism: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new ProgressMonitor(totalTaskCount: -1, taskTimeout: TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void The_percentage_follows_the_completed_tasks_with_a_long_timeout()
    {
        var monitor = new ProgressMonitor(totalTaskCount: 4, taskTimeout: TimeSpan.FromHours(1), maxDegreeOfParallelism: 2);
        monitor.IncrementCompleted();
        monitor.IncrementCompleted();
        Assert.AreEqual(50, monitor.Progress.Percentage);
        Assert.AreEqual(2, monitor.Progress.Completed);
    }
}
