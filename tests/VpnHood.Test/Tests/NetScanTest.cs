using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Server;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Test.Extensions;

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

        var session = server.GetSession(client);

        var tcpClient1 = new TcpClient();
        await tcpClient1.ConnectAsync(MockEps.HttpV4EndPoint1, TestCt);
        tcpClient1.GetStream().WriteByte((byte)'G'); // send some data otherwise client may not create the channel
        await AssertEqualsWait(1, () => session.TcpChannelCount);

        // NetScan error
        Log("Creating the second connection");
        var tcpClient2 = new TcpClient();
        await tcpClient2.ConnectAsync(MockEps.HttpV4EndPoint2, TestCt);

        Log( "Sending data on the second connection");
        tcpClient2.GetStream().WriteByte((byte)'G'); // send some data otherwise client may not create the channel
        
        Log("Waiting for the second connection to be closed by server");
        await AssertEqualsWait(1, ()=> session.NetScanErrorCount);
    }


    [TestMethod]
    public async Task Detect_by_UnitTest()
    {
        var netScanDetector = new NetScanDetector(3, TimeSpan.FromSeconds(1));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.1").ToValue()));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.1").ToValue()));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.2").ToValue()));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.3").ToValue()));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.3").ToValue()));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:441").ToValue()));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:442").ToValue()));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:443").ToValue()));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:443").ToValue()));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("100.10.11.1:443").ToValue()));

        Assert.IsFalse(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.4").ToValue()));
        Assert.IsFalse(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:444").ToValue()));

        await Task.Delay(TimeSpan.FromSeconds(1), TestCt);
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.10.4").ToValue()));
        Assert.IsTrue(netScanDetector.Verify(IPEndPoint.Parse("10.10.11.1:444").ToValue()));
    }
}