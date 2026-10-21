using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Test;

namespace VpnHood.AppLib.Test;

[TestClass]
public class TestAssembly
{
    [AssemblyInitialize]
    public static void AssemblyInit(TestContext _)
    {
        // two assembly will be loaded if test run parallel, so overrode to prevent conflict in cleanup
        TestHelper.AssemblyWorkingPath = Path.Combine(Path.GetTempPath(), "vh", "VpnHood.AppLib.Test");

        // the parallel tests' clients share the process, and with it the gate on new connections
        ConnectorService.ConnectGateCapacity = 64;
    }

    [AssemblyCleanup]
    public static void AssemblyCleanup()
    {
        TestHelper.AssemblyCleanup();
    }
}