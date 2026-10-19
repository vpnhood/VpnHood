using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class VhInvokerTest : TestBase
{
    [TestMethod]
    public void A_failure_is_logged_at_the_level_given()
    {
        var message = TestLogCapture.Instance.CreateMessage();

        VhInvoker.TryInvoke("fail on purpose", LogLevel.Warning, () => throw new IOException(message));

        CollectionAssert.AreEqual(new[] { LogLevel.Warning }, TestLogCapture.Instance.GetLevels(message).ToArray());
    }

    [TestMethod]
    public async Task A_cancellation_is_logged_at_Debug_whatever_the_level_given()
    {
        var message = TestLogCapture.Instance.CreateMessage();

        await VhInvoker.TryInvokeAsync("be cancelled on purpose", LogLevel.Error,
            () => Task.FromException(new OperationCanceledException(message)));

        CollectionAssert.AreEqual(new[] { LogLevel.Debug }, TestLogCapture.Instance.GetLevels(message).ToArray());
    }
}
