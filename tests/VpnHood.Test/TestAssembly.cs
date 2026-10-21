using VpnHood.Core.Client.ConnectorServices;

namespace VpnHood.Test;

[TestClass]
public class TestAssembly
{
    [AssemblyInitialize]
    public static void AssemblyInit(TestContext _)
    {
        // the parallel tests' clients share the process, and with it the gate on new connections
        ConnectorService.ConnectGateCapacity = 64;
    }

    [AssemblyCleanup]
    public static void AssemblyCleanup()
    {
        TestHelper.AssemblyCleanup();
    }
}