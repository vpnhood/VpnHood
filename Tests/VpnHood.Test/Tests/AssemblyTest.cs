using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VpnHood.Test.Tests;

[TestClass]
public class AssemblyTest
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