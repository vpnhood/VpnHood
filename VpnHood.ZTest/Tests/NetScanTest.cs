using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Frameworks;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using VpnHood.Common.Logging;
using VpnHood.Server;
using VpnHood.Server.Providers.FileAccessServerProvider;

namespace VpnHood.Test.Tests;

[TestClass]
public class NetScanTest
{
    [TestInitialize]
    public void Initialize()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
    }

    [TestMethod]
    public void Reject_by_server()
    {
        using var httpClient = new HttpClient();

        // create server
        var fileAccessServerOptions = TestHelper.CreateFileAccessServerOptions();
        fileAccessServerOptions.SessionOptions.NetScanTimeout = TimeSpan.FromSeconds(100);
        fileAccessServerOptions.SessionOptions.NetScanLimit= 1;
        using var server = TestHelper.CreateServer(fileAccessServerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        using var client = TestHelper.CreateClient(token);

        TestHelper.Test_Https(uri: TestHelper.TEST_HttpsUri1);
        try
        {
            TestHelper.Test_Https(uri: TestHelper.TEST_HttpsUri2);
            Assert.Fail("NetScan should reject this request.");
        }
        catch (Exception ex)
        {

        }

    }


    [TestMethod]
    public async Task Detect_by_UnitTest()
    {
        var netScanDetector = new NetScanDetector(3, TimeSpan.FromSeconds(1));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.1")));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.1")));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.2")));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.3")));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.3")));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:441")));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:442")));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:443")));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:443")));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("100.10.11.1:443")));

        Assert.IsFalse(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.4")));
        Assert.IsFalse(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:444")));

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.4")));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:444")));
    }

}