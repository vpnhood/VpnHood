using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Test;

namespace VpnHood.AppLib.Test;

[TestClass]
public class TestAssembly
{
    [AssemblyInitialize]
    public static void AssemblyInit(TestContext _)
    {
        TestHelper.Init();
    }

    [AssemblyCleanup]
    public static void AssemblyCleanup()
    {
        TestHelper.Cleanup();
    }
}