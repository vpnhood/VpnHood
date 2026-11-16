using VpnHood.Test;

namespace VpnHood.AppLogic.Test;

public abstract class TestAppBase : TestBase
{
    protected TestAppHelper TestAppHelper => (TestAppHelper)TestHelper;

    protected override TestHelper CreateTestHelper()
    {
        return new TestAppHelper(TestCt);
    }
}