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
        public int ReportedCount { get; private set; }
        public TestEventReporter(ILogger logger, string message)
            : base(logger, message)
        {
        }

        protected override void Report()
        {
            Console.WriteLine(DateTime.Now);
            base.Report();
            ReportedCount++;
        }
    }

    [TestMethod]
    public async Task EventReportCounter()
    {
        using var reportCounter = new TestEventReporter(NullLogger.Instance, "");
        reportCounter.WatchDogChecker.Interval = TimeSpan.FromMilliseconds(500);

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

        reportCounter.WatchDogChecker.Interval = WatchDogRunner.Default.Interval / 2;
        await Task.Delay(reportCounter.WatchDogChecker.Interval);
        reportCounter.Raised(); // immediate
        Assert.AreEqual(5, reportCounter.TotalEventCount);
        Assert.AreEqual(3, reportCounter.ReportedCount);
    }
}