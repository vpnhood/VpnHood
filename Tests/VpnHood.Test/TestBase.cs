using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;

namespace VpnHood.Test;

public abstract class TestBase
{
    protected TestHelper TestHelper { get; private set; } = null!;
    protected virtual CancellationToken TestCt => CancellationToken.None;

    [TestInitialize]
    public void TestInitialize()
    {
        TestHelper = CreateTestHelper();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        TestHelper.Dispose();
    }

    protected static void Log(string message)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Test, message);
    }

    protected TestWebServerMockEps MockEps => TestHelper.WebServer.MockEps;
    protected virtual TestHelper CreateTestHelper() => new();

    protected Task AssertEqualsWait<TValue>(TValue expectedValue, Func<TValue> valueFactory,
        string? message = null, int timeout = 5000, bool noTimeoutOnDebugger = true)
    {
        return VhTestUtil.AssertEqualsWait(
            expectedValue, valueFactory, message, timeout, noTimeoutOnDebugger, TestCt);
    }
}