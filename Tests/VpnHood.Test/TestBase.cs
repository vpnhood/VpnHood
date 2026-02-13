using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;

namespace VpnHood.Test;

public abstract class TestBase
{
    protected TestHelper TestHelper { get; private set; } = null!;
    protected virtual CancellationToken TestCancellationToken => CancellationToken.None;

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

    protected void Log(string message)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Test, message);
    }

    protected IPAddress MockIp(IPAddress ipAddress) => TestHelper.NetFilterIps.MapToRemote(ipAddress);
    protected IPEndPoint MockIp(IPEndPoint endPoint) => TestHelper.NetFilterIps.MapToRemote(endPoint);
    protected Uri MockIp(Uri endPoint) => TestHelper.NetFilterIps.MapToRemote(endPoint);

    protected virtual TestHelper CreateTestHelper() => new();
}