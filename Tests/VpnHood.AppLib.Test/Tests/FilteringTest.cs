using System.Net;
using System.Net.Quic;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Test.Dom;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Test;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class FilteringTest : TestAppBase
{
    [TestMethod]
    public async Task Domains_IncludeExclude_Https()
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper);
        var app = appDom.App;
        app.UserSettings.UseSplitDomain = true;
        app.SettingsService.SplitDomainSettings.Includes = MockEps.HttpsUrl1.Host;
        app.SettingsService.SplitDomainSettings.Excludes = MockEps.HttpsUrl2.Host;

        // domain filter should have upper hand.
        // Here we force IpFilter to include HttpsUrl2 and exclude HttpsUrl1
        app.SettingsService.SplitIpSettings.AppIncludes = MockEps.HttpsV4EndPoint2.Address.ToString();

        // connect
        await appDom.Connect(cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // test includes
        var oldStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        await TestHelper.Test_Https(uri: MockEps.HttpsUrl1);
        var newStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        Assert.AreEqual(oldStat.StreamTunnelledCount + 1, newStat.StreamTunnelledCount);
        Assert.AreEqual(oldStat.StreamPassthruCount, newStat.StreamPassthruCount);

        // test excludes
        oldStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        await TestHelper.Test_Https(uri: MockEps.HttpsUrl2);
        newStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        Assert.AreEqual(oldStat.StreamTunnelledCount, newStat.StreamTunnelledCount);
        Assert.AreEqual(oldStat.StreamPassthruCount + 1, newStat.StreamPassthruCount);
    }

    [TestMethod]
    public async Task Domains_IncludeExclude_Quic()
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper);
        var app = appDom.App;
        app.UserSettings.UseSplitDomain = true;
        app.SettingsService.SplitDomainSettings.Includes = MockEps.QuicUrl1.Host;
        app.SettingsService.SplitDomainSettings.Excludes = MockEps.QuicUrl2.Host;

        // domain filter should have upper hand.
        // Here we force IpFilter to include QuicUrl2 and exclude QuicUrl1
        app.SettingsService.SplitIpSettings.AppIncludes = MockEps.QuicEndPoint2.Address.ToString();

        // connect
        await appDom.Connect(cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // test includes
        var oldStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        await TestHelper.Test_Quic(uri: MockEps.QuicUrl1);
        var newStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        Assert.AreEqual(oldStat.StreamTunnelledCount + 1, newStat.StreamTunnelledCount);
        Assert.AreEqual(oldStat.StreamPassthruCount, newStat.StreamPassthruCount);

        // test excludes
        oldStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        await TestHelper.Test_Quic(uri: MockEps.QuicUrl2);
        newStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        Assert.AreEqual(oldStat.StreamTunnelledCount, newStat.StreamTunnelledCount);
        Assert.AreEqual(oldStat.StreamPassthruCount + 1, newStat.StreamPassthruCount);
    }


    [TestMethod]
    public async Task Domains_Block_Https()
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper);
        var app = appDom.App;
        app.UserSettings.UseSplitDomain = true;

        // block domain1
        app.SettingsService.SplitDomainSettings.Blocks = MockEps.HttpsUrl1.Host;

        // connect
        await appDom.Connect(cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // blocked HTTPS should fail
        Log("Testing blocked HTTPS domain...");
        var httpsResult = await TestHelper.Test_Https(uri: MockEps.HttpsUrl1, throwError: false, timeout: TimeSpan.FromSeconds(1));
        Assert.IsFalse(httpsResult, "HTTPS to blocked domain should fail.");

        // non-blocked HTTPS should still work
        Log("Testing non-blocked HTTPS domain...");
        var oldStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        await TestHelper.Test_Https(uri: MockEps.HttpsUrl2);
        var newStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        Assert.AreEqual(oldStat.StreamTunnelledCount + 1, newStat.StreamTunnelledCount);
    }

    [TestMethod]
    public async Task Domains_Block_Quic()
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper);
        var app = appDom.App;
        app.UserSettings.UseSplitDomain = true;

        // block domain1
        app.SettingsService.SplitDomainSettings.Blocks = MockEps.QuicUrl1.Host;

        // connect
        await appDom.Connect(cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // blocked QUIC should fail
        Log("Testing blocked QUIC domain...");
        var ex = await Assert.ThrowsExactlyAsync<QuicException>(() =>
             TestHelper.Test_Quic(uri: MockEps.QuicUrl1, timeout: TimeSpan.FromSeconds(1)));
        Assert.AreEqual(QuicError.ConnectionTimeout, ex.QuicError);

        // non-blocked QUIC should still work
        Log("Testing non-blocked QUIC domain...");
        var oldStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        await TestHelper.Test_Quic(uri: MockEps.QuicUrl2);
        var newStat = await app.GetSessionStatusAsync(cancellationToken: TestCt);
        Assert.AreEqual(oldStat.StreamPassthruCount, newStat.StreamPassthruCount);
        Assert.AreEqual(oldStat.StreamTunnelledCount + 1, newStat.StreamTunnelledCount);
    }

    [TestMethod]
    public async Task Ips_IncludeExclude()
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper);
        var app = appDom.App;

        // target1
        var httpsUrl1 = MockEps.HttpsUrl1;
        var udpEchoEndPoint1 = MockEps.UdpV4EndPoint1;
        var targetIps1 = new[] { new IpRange(IPAddress.Parse(MockEps.HttpUrl1.Host)), new IpRange(udpEchoEndPoint1.Address) };

        // target1
        var httpsUrl2 = MockEps.HttpsUrl2;
        var udpEchoEndPoint2 = MockEps.UdpV4EndPoint2;
        var targetIps2 = new[] { new IpRange(IPAddress.Parse(MockEps.HttpUrl2.Host)), new IpRange(udpEchoEndPoint2.Address) };

        // ************
        // *** TEST ***: Test Include ip filter
        app.SettingsService.SplitIpSettings.AppIncludes = targetIps1.ToText();
        app.SettingsService.SplitIpSettings.AppExcludes = targetIps2.ToText();
        await app.Connect(appDom.ClientProfile.ClientProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);
        await TestHelper.Test_Ping(ipAddress: MockEps.PingV4Address1);

        Log("Starting IpFilters_TestInclude...");
        await IpFilters_AssertExclude(TestHelper, app, udpEchoEndPoint2, httpsUrl2);
        await IpFilters_AssertInclude(TestHelper, app, udpEchoEndPoint1, httpsUrl1);
        await app.Disconnect();

        // ************
        // *** TEST ***: Reverse include/exclude list, then target1 should be excluded and target2 should be included.
        app.SettingsService.SplitIpSettings.AppIncludes = targetIps2.ToText();
        app.SettingsService.SplitIpSettings.AppExcludes = targetIps1.ToText();
        await app.Connect(appDom.ClientProfile.ClientProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        Log("Starting IpFilters_TestExclude...");
        await IpFilters_AssertInclude(TestHelper, app, udpEchoEndPoint2, httpsUrl2);
        await IpFilters_AssertExclude(TestHelper, app, udpEchoEndPoint1, httpsUrl1);
        await app.Disconnect();
    }

    [TestMethod]
    public async Task Ips_Block()
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper);
        var app = appDom.App;

        // target1: will be blocked
        var httpsUrl1 = MockEps.HttpsUrl1;
        var udpEchoEndPoint1 = MockEps.UdpV4EndPoint1;
        var blockedIps = new[] { new IpRange(IPAddress.Parse(MockEps.HttpUrl1.Host)), new IpRange(udpEchoEndPoint1.Address) };

        // target2: should still work (included)
        var httpsUrl2 = MockEps.HttpsUrl2;
        var udpEchoEndPoint2 = MockEps.UdpV4EndPoint2;

        // ************
        // *** TEST ***: Block target1 IPs via AppBlocks
        app.SettingsService.SplitIpSettings.AppBlocks = blockedIps.ToText();
        await app.Connect(appDom.ClientProfile.ClientProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // blocked HTTPS should fail
        Log("Testing blocked HTTPS...");
        var httpsResult = await TestHelper.Test_Https(uri: httpsUrl1, throwError: false, timeout: TimeSpan.FromSeconds(1));
        Assert.IsFalse(httpsResult, "HTTPS to blocked IP should fail.");

        // blocked UDP should fail
        Log("Testing blocked UDP...");
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            TestHelper.Test_UdpEcho(udpEchoEndPoint1, timeout: TimeSpan.FromSeconds(1)));

        // non-blocked target should still work
        Log("Testing non-blocked HTTPS...");
        await IpFilters_AssertInclude(TestHelper, app, udpEchoEndPoint2, httpsUrl2);
        await app.Disconnect();
    }

    public static async Task IpFilters_AssertInclude(TestHelper testHelper, VpnHoodApp app,
        IPEndPoint? udpEchoEndPint, Uri? url, int receiveDelta = 1000)
    {
        // Echo
        if (udpEchoEndPint != null) {
            var oldStat = await app.GetSessionStatusAsync();
            await testHelper.Test_UdpEcho(udpEchoEndPint);
            var newStat = await app.GetSessionStatusAsync();
            Assert.AreNotEqual(oldStat.SessionTraffic, newStat.SessionTraffic);
            Assert.AreEqual(oldStat.SessionSplitTraffic, newStat.SessionSplitTraffic);
        }

        // Http
        if (url != null) {
            var oldStat = await app.GetSessionStatusAsync();
            await testHelper.Test_Https(url);
            var newStat = await app.GetSessionStatusAsync();
            Assert.AreNotEqual(oldStat.SessionTraffic.Received, newStat.SessionTraffic.Received, delta: receiveDelta);
            Assert.AreNotEqual(oldStat.SessionTraffic.Sent, newStat.SessionTraffic.Sent, delta: 50);
            Assert.AreEqual(oldStat.SessionSplitTraffic.Received, newStat.SessionSplitTraffic.Received, delta: receiveDelta);
            Assert.AreEqual(oldStat.SessionSplitTraffic.Sent, newStat.SessionSplitTraffic.Sent, delta: 50);
        }
    }

    public static async Task IpFilters_AssertExclude(TestHelper testHelper, VpnHoodApp app,
        IPEndPoint? udpEchoEndPint, Uri? url, int receiveDelta = 1000)
    {
        // NameServer
        if (udpEchoEndPint != null) {
            var oldStat = await app.GetSessionStatusAsync();
            await testHelper.Test_UdpEcho(udpEchoEndPint);
            var newStat = await app.GetSessionStatusAsync();

            Assert.AreEqual(oldStat.SessionTraffic, newStat.SessionTraffic,
                $"Udp to {udpEchoEndPint} should go to tunnel.");

            Assert.AreNotEqual(oldStat.SessionSplitTraffic, newStat.SessionSplitTraffic,
                $"Udp to {udpEchoEndPint} should not be split.");
        }

        // Http
        if (url != null) {
            var oldStat = await app.GetSessionStatusAsync();
            await testHelper.Test_Https(url);
            var newStat = await app.GetSessionStatusAsync();
            Assert.AreEqual(oldStat.SessionTraffic.Received, newStat.SessionTraffic.Received, delta: receiveDelta);
            Assert.AreEqual(oldStat.SessionTraffic.Sent, newStat.SessionTraffic.Sent, delta: 50);
            Assert.AreNotEqual(oldStat.SessionSplitTraffic.Received, newStat.SessionSplitTraffic.Received, delta: receiveDelta);
            Assert.AreNotEqual(oldStat.SessionSplitTraffic.Sent, newStat.SessionSplitTraffic.Sent, delta: 50);
        }
    }
}
