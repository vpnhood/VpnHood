using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Common.Logging;

namespace VpnHood.Test.Tests
{
    [TestClass]
    public class AssemblyTest
    {
        [AssemblyInitialize]
        public static void AssemblyInit(TestContext _)
        {
            VhLogger.IsDiagnoseMode = true;
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            TestHelper.Cleanup();
        }
    }
}