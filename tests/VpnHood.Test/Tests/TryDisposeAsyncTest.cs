using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class TryDisposeAsyncTest : TestBase
{
    [TestMethod]
    public async Task A_failed_close_is_logged_at_the_level_given()
    {
        var message = TestLogCapture.Instance.CreateMessage();
        var disposable = new TestAsyncDisposable { CloseFailure = new IOException(message) };

        await disposable.TryDisposeAsync(LogLevel.Warning);

        CollectionAssert.AreEqual(new[] { LogLevel.Warning }, TestLogCapture.Instance.GetLevels(message).ToArray());
    }

    [TestMethod]
    public async Task A_close_that_fails_after_its_timeout_is_still_logged()
    {
        var message = TestLogCapture.Instance.CreateMessage();
        var disposable = new TestAsyncDisposable { HoldClose = true, CloseFailure = new IOException(message) };

        await disposable.TryDisposeAsync(TimeSpan.FromMilliseconds(100), LogLevel.Warning);
        disposable.ReleaseClose();

        await VhTestUtil.AssertEqualsWait(1, () => TestLogCapture.Instance.GetLevels(message).Count,
            "The close's failure should be logged once.", cancellationToken: TestCt);
        Assert.AreEqual(LogLevel.Warning, TestLogCapture.Instance.GetLevels(message).Single());
    }
}
