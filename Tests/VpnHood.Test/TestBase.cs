using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;

namespace VpnHood.Test;

public abstract class TestBase
{
    protected TestHelper TestHelper { get; private set; } = null!;

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

    protected void Log(string message)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Test, message);
    }

    protected virtual TestHelper CreateTestHelper() => new();
}