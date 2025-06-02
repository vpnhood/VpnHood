using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class UtilTest : TestBase
{
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
    public async Task EventReportCounter()
    {
        // Test with LogLevel.Information
        VhLogger.MinLogLevel = LogLevel.Information;

        using var reportCounter = new TestEventReporter("UnitTest", period: TimeSpan.FromMilliseconds(1000));

        Assert.AreEqual(0, reportCounter.ReportedCount);

        reportCounter.Raise(); // report
        Assert.AreEqual(1, reportCounter.TotalEventCount);
        await VhTestUtil.AssertEqualsWait(1, ()=>reportCounter.ReportedCount);

        reportCounter.Raise(); // wait
        reportCounter.Raise(); // wait
        reportCounter.Raise(); // wait
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        Assert.AreEqual(1, reportCounter.ReportedCount);

        // wait for the next report
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        await VhTestUtil.AssertEqualsWait(2, ()=>reportCounter.ReportedCount);

        reportCounter.Raise(); // immediate
        Assert.AreEqual(5, reportCounter.TotalEventCount);
        Assert.AreEqual(2, reportCounter.ReportedCount);
        await VhTestUtil.AssertEqualsWait(3, ()=>reportCounter.ReportedCount);
    }
}