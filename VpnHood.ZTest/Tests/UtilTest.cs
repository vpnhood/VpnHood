using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class UtilTest
{
    private class TestEventReporter : EventReporter
    {
        public int ReportedCount { get; private set; }
        public TestEventReporter(ILogger logger, string message)
            : base(logger, message)
        {
        }

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
        reportCounter.JobSection.Interval = TimeSpan.FromMilliseconds(500);

        Assert.AreEqual(0, reportCounter.ReportedCount);
        
        reportCounter.Raised(); // report
        Assert.AreEqual(1, reportCounter.TotalEventCount);
        Assert.AreEqual(1, reportCounter.ReportedCount);

        reportCounter.Raised(); // wait
        reportCounter.Raised(); // wait
        reportCounter.Raised(); // wait
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        Assert.AreEqual(1, reportCounter.ReportedCount);

        await Task.Delay(1000);
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        Assert.AreEqual(2, reportCounter.ReportedCount);

        reportCounter.JobSection.Interval = JobRunner.Default.Interval / 2;
        await Task.Delay(reportCounter.JobSection.Interval);
        reportCounter.Raised(); // immediate
        Assert.AreEqual(5, reportCounter.TotalEventCount);
        Assert.AreEqual(3, reportCounter.ReportedCount);
    }
}