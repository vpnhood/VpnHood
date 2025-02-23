using Microsoft.VisualStudio.TestTools.UnitTesting;

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

    protected virtual TestHelper CreateTestHelper() => new();
}