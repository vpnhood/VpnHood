using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Toolkit.Jobs;

namespace VpnHood.Test.Tests;

[TestClass]
public class UtilTest : TestBase
{
    private class TestEventReporter(ILogger logger, string message) : EventReporter(logger, message)
    {
        public int ReportedCount { get; private set; }

        protected override void Report()
        {
            base.Report();
            ReportedCount++;
        }
    }

    [TestMethod]
    public async Task EventReportCounter()
    {
        using var reportCounter = new TestEventReporter(VhLogger.Instance, "UnitTest");
        EventReporter.IsDiagnosticMode = false;
        reportCounter.JobSection.Interval = TimeSpan.FromMilliseconds(500);

        Assert.AreEqual(0, reportCounter.ReportedCount);

        reportCounter.Raise(); // report
        Assert.AreEqual(1, reportCounter.TotalEventCount);
        Assert.AreEqual(1, reportCounter.ReportedCount);

        reportCounter.Raise(); // wait
        reportCounter.Raise(); // wait
        reportCounter.Raise(); // wait
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        Assert.AreEqual(1, reportCounter.ReportedCount);

        await Task.Delay(1000);
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        Assert.AreEqual(2, reportCounter.ReportedCount);

        reportCounter.JobSection.Interval = JobRunner.Default.Interval / 2;
        await Task.Delay(reportCounter.JobSection.Interval);
        reportCounter.Raise(); // immediate
        Assert.AreEqual(5, reportCounter.TotalEventCount);
        Assert.AreEqual(3, reportCounter.ReportedCount);
    }
}