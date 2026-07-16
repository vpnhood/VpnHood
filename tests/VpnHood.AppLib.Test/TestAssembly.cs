using VpnHood.Test;

namespace VpnHood.AppLib.Test;

[TestClass]
public class TestAssembly
{
    [AssemblyInitialize]
    public static void AssemblyInit(TestContext _)
    {
        // two assembly will be loaded if test run parallel, so overrode to prevent conflict in cleanup
        TestHelper.AssemblyWorkingPath = Path.Combine(Path.GetTempPath(), "VpnHood.AppLib.Test");
    }

    [AssemblyCleanup]
    public static void AssemblyCleanup()
    {
        TestHelper.AssemblyCleanup();
    }
}