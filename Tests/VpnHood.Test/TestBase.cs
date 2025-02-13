using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Common.Logging;

namespace VpnHood.Test;

public abstract class TestBase
{
    protected TestHelper TestHelper { get; private set; } = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
        TestHelper = CreateTestHelper();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        TestHelper.Dispose();
    }

    protected virtual TestHelper CreateTestHelper() => new();
}