namespace VpnHood.Test;

[TestClass]
public class TestAssembly
{
    [AssemblyInitialize]
    public static void AssemblyInit(TestContext _)
    {
    }

    [AssemblyCleanup]
    public static void AssemblyCleanup()
    {
        TestHelper.AssemblyCleanup();
    }
}