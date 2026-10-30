using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;

namespace VpnHood.Test;

public abstract class TestBase
{
    private TestHelper? _testHelper;
    protected TestHelper TestHelper =>
        _testHelper ?? throw new InvalidOperationException("The TestHelper is created in TestInitialize.");
    public required TestContext TestContext { get; set; }
    protected virtual CancellationToken TestCt => TestContext.CancellationToken;

    [TestInitialize]
    public void TestInitialize()
    {
        _testHelper = CreateTestHelper();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        // null when TestInitialize failed, whose error stays the one reported
        _testHelper?.Dispose();
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

    protected Task AssertEqualsWait<TValue>(TValue expectedValue, Func<Task<TValue>> valueFactory,
        string? message = null, int timeout = 5000, bool noTimeoutOnDebugger = true)
    {
        return VhTestUtil.AssertEqualsWait(
            expectedValue, valueFactory, message, timeout, noTimeoutOnDebugger, TestCt);
    }
}