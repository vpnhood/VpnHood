using System.Net.Sockets;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class NetProtectTest : TestBase
{
    [TestMethod]
    public async Task MaxTcpWaitConnect_reject()
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.TcpConnectTimeout = TimeSpan.FromSeconds(1);
        fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount = 0;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        try {
            // a generous timeout: under parallel load the SYN can take seconds to reach the stack,
            // and the fetch must outlive that delay to receive the rejection instead of timing out
            await TestHelper.Test_Https(timeout: TimeSpan.FromSeconds(15));
            Assert.Fail("Exception expected!");
        }
        catch (Exception ex) {
            Assert.AreEqual(nameof(HttpRequestException), ex.GetType().Name);
        }
    }

    [TestMethod]
    public async Task MaxTcpWaitConnect_accept()
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.TcpConnectTimeout = TimeSpan.FromSeconds(1);
        fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount = 1;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.ChannelProtocol = ChannelProtocol.Udp;
        clientOptions.UseTcpProxy = true;
        await using var client = await TestHelper.CreateClient(clientOptions);

        await TestHelper.Test_Https();
        await TestHelper.Test_Https();
    }

    [TestMethod]
    public async Task MaxUdpDnsPacketsPerSecond_by_proxy()
    {
        await Test_MaxUdpDnsPacketsPerSecond(vpnAdapter: null);
    }

    [TestMethod]
    public async Task MaxUdpDnsPacketsPerSecond_by_vpn_adapter()
    {
        using var vpnAdapter = new TestUdpServerVpnAdapter();
        var adapterUsed = false;
        vpnAdapter.PacketReceived += (_, _) => adapterUsed = true;

        await Test_MaxUdpDnsPacketsPerSecond(vpnAdapter);
        Assert.IsTrue(adapterUsed);
    }

    [TestMethod]
    public async Task WindowRateLimiter_limits_each_window()
    {
        var windowRateLimiter = new WindowRateLimiter(3, TimeSpan.FromHours(1));
        Assert.IsTrue(windowRateLimiter.TryAcquire());
        Assert.IsTrue(windowRateLimiter.TryAcquire());
        Assert.IsTrue(windowRateLimiter.TryAcquire());
        Assert.IsFalse(windowRateLimiter.TryAcquire());

        // the next window passes again
        var shortRateLimiter = new WindowRateLimiter(1, TimeSpan.FromMilliseconds(200));
        Assert.IsTrue(shortRateLimiter.TryAcquire());
        await AssertEqualsWait(true, shortRateLimiter.TryAcquire);
    }

    private async Task Test_MaxUdpDnsPacketsPerSecond(IVpnAdapter? vpnAdapter)
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.MaxUdpDnsPacketsPerSecond = 5;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions, vpnAdapter: vpnAdapter);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        // a flood from one port: a few seconds' worth comes back at most, as the burst may span a second's turn
        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        for (var i = 0; i < 100; i++)
            await udpClient.SendAsync(new byte[10], MockEps.UdpNsEchoEndPoint1, TestCt);

        var echoCount = await CountReceived(udpClient, quietTime: TimeSpan.FromSeconds(2));
        Assert.IsTrue(echoCount is > 0 and <= 25, $"EchoCount: {echoCount}");

        // the rest of UDP is not limited
        await TestHelper.Test_UdpEcho();
    }

    // the datagrams that arrive until none has for the quiet time
    private async Task<int> CountReceived(UdpClient udpClient, TimeSpan quietTime)
    {
        var count = 0;
        while (true) {
            using var quietCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
            quietCts.CancelAfter(quietTime);
            try {
                await udpClient.ReceiveAsync(quietCts.Token);
            }
            catch (OperationCanceledException) {
                TestCt.ThrowIfCancellationRequested();
                return count;
            }

            count++;
        }
    }
}