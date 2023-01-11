using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using VpnHood.Common.Timing;
using VpnHood.Common.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class UtilTest
{
    private class TestEventReporter : EventReporter
    {
        public int ReportCount { get; private set; }
        public TestEventReporter(ILogger logger, string message)
            : base(logger, message)
        {
        }

        protected override void Report()
        {
            base.Report();
            ReportCount++;
        }
    }

    [TestMethod]
    public async Task EventReportCounter()
    {
        using var reportCounter = new TestEventReporter(NullLogger.Instance, "");
        reportCounter.WatchDogChecker.Interval = TimeSpan.FromMilliseconds(500);

        Assert.AreEqual(0, reportCounter.ReportCount);
        
        reportCounter.Raised(); // report
        Assert.AreEqual(1, reportCounter.TotalEventCount);
        Assert.AreEqual(1, reportCounter.ReportCount);

        reportCounter.Raised(); // wait
        reportCounter.Raised(); // wait
        reportCounter.Raised(); // wait
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        Assert.AreEqual(1, reportCounter.ReportCount);

        await Task.Delay(1000);
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        Assert.AreEqual(2, reportCounter.ReportCount);

        reportCounter.WatchDogChecker.Interval = WatchDogRunner.Default.Interval / 4;
        await Task.Delay(WatchDogRunner.Default.Interval / 2);
        reportCounter.Raised(); // immediate
        Assert.AreEqual(5, reportCounter.TotalEventCount);
        Assert.AreEqual(3, reportCounter.ReportCount);
    }
}