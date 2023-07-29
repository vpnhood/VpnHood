using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Common.Logging;

namespace VpnHood.Test.Tests;

public abstract class TestBase
{
    [TestInitialize]
    public void TestInitialize()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
    }
}