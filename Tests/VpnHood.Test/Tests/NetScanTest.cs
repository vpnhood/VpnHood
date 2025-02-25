using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Server;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class NetScanTest : TestBase
{
    [TestMethod]
    public async Task Reject_by_server()
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.NetScanTimeout = TimeSpan.FromSeconds(100);
        fileAccessManagerOptions.SessionOptions.NetScanLimit = 1;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        var tcpClient1 = new TcpClient();
        await tcpClient1.ConnectAsync(TestConstants.TcpEndPoint1);
        try {
            await VhUtils.RunTask(tcpClient1.GetStream().ReadAsync(new byte[100]).AsTask(), TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) {
            Assert.AreEqual(nameof(TimeoutException), ex.GetType().Name);
        }

        // NetScan error
        var tcpClient2 = new TcpClient();
        await tcpClient2.ConnectAsync(TestConstants.TcpEndPoint2);
        var res = await tcpClient2.GetStream().ReadAsync(new byte[100]);
        Assert.AreEqual(0, res, "NetScan should close this request.");
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