using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Ads;
using VpnHood.Test.Extensions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// The client's live settings, its single connect, and its session's wait for an ad
[TestClass]
public class ClientSettingsTest : TestBase
{
    [TestMethod]
    public async Task The_live_settings_start_from_the_options_and_a_change_before_the_connect_reaches_the_session()
    {
        await using var server = await TestHelper.CreateServer();
        var options = TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server));
        options.UseTcpForwarding = true;
        options.DropUdp = true;
        options.DropQuic = true;
        options.UnroutedIpMode = SplitUnsupportedIpMode.Exclude;
        options.UnsupportedIpV6Mode = SplitUnsupportedIpMode.Exclude; // not its default, Block
        await using var client = await TestHelper.CreateClient(options, autoConnect: false);

        // they read false whatever the options said
        Assert.IsTrue(client.UseTcpForwarding);
        Assert.IsTrue(client.DropUdp);
        Assert.IsTrue(client.DropQuic);
        Assert.AreEqual(SplitUnsupportedIpMode.Exclude, client.UnsupportedIpV6Mode);

        // Set before the connect: the session started from the options all the same, and the build put the options'
        // mode back
        client.DropQuic = false;
        client.UnroutedIpMode = SplitUnsupportedIpMode.Block;
        await client.Connect(TestCt);

        var session = (ClientSession)client.RequiredSession;
        Assert.IsFalse(session.DropQuic);
        Assert.IsTrue(session.DropUdp);
        Assert.AreEqual(SplitUnsupportedIpMode.Block, client.UnroutedIpMode);
        Assert.AreEqual(SplitUnsupportedIpMode.Exclude, client.UnsupportedIpV6Mode);
    }

    [TestMethod]
    public async Task A_drop_asked_for_comes_back_with_TCP_forwarding()
    {
        // a server that takes TCP packets, so TCP forwarding can go off
        using var vpnAdapter = new TestUdpServerVpnAdapter();
        await using var server = await TestHelper.CreateServer(TestHelper.CreateFileAccessManagerOptions(),
            vpnAdapter: vpnAdapter);

        var options = TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server), useTcpForwarding: true);
        options.DropQuic = true;
        await using var client = await TestHelper.CreateClient(options);
        Assert.IsTrue(client.RequiredSession.Status.IsDropQuic);

        // Dropping QUIC needs TCP forwarding, so it is off while forwarding is: the session forgot it was asked for,
        // and it stayed off once forwarding was back
        client.UseTcpForwarding = false;
        Assert.IsFalse(client.RequiredSession.Status.IsDropQuic);
        client.UseTcpForwarding = true;
        Assert.IsTrue(client.RequiredSession.Status.IsDropQuic);
    }

    [TestMethod]
    public async Task A_longest_channel_age_below_the_shortest_is_out_of_range()
    {
        await using var server = await TestHelper.CreateServer();
        var options = TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server));
        options.MinPacketChannelTimespan = TimeSpan.FromMinutes(10);
        options.MaxPacketChannelTimespan = TimeSpan.FromMinutes(5);

        // nothing was null, and the exception said so
        var ex = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            TestHelper.CreateClient(options, autoConnect: false));

        Assert.AreEqual("options", ex.ParamName);
    }

    [TestMethod]
    public async Task A_second_connect_fails_alone_and_leaves_the_connected_session()
    {
        await using var server = await TestHelper.CreateServer();
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)));

        // it failed with a plain exception, whose handling closed the client and its session
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.Connect(TestCt));
        Assert.AreEqual(ClientState.Connected, client.State);
        Assert.IsNull(client.LastException);
        await TestHelper.Test_Https();
    }

    [TestMethod]
    public async Task A_wait_for_an_ad_that_its_starter_cancels_ends_for_every_caller()
    {
        await using var server = await TestHelper.CreateServer();
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)));

        var session = (ClientSession)client.RequiredSession;
        var adHandler = session.AdHandler;

        using var starterCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var starterWait = adHandler.WaitForAd(starterCts.Token);
        var joinerWait = adHandler.WaitForAd(TestCt);
        Assert.IsTrue(session.PassthroughState.PassthroughForAd);

        // Its starter cancels: the wait went on, with the session in the ad's state, and the next caller joined it
        await starterCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => starterWait);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            joinerWait.WaitAsync(TimeSpan.FromSeconds(5), TestCt));

        Assert.IsFalse(adHandler.IsWaitingForAd);
        Assert.IsFalse(session.PassthroughState.PassthroughForAd);
        await client.WaitForState(ClientState.Connected, TestCt);

        // a new wait starts, and an ad's outcome ends it
        var nextWait = adHandler.WaitForAd(TestCt);
        Assert.IsTrue(adHandler.IsWaitingForAd);
        adHandler.SetAdOk();
        await nextWait.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsFalse(session.PassthroughState.PassthroughForAd);
    }

    // An ad during a session went through the tunnel at once where the app's networks could, and around it otherwise. It
    // goes around the tunnel first, then through it where the app allows it; the try through the tunnel resets no
    // connection
    [TestMethod]
    public async Task An_ad_during_a_session_goes_around_the_tunnel_then_through_it()
    {
        // a server that keeps its connection open
        var localEndPoint = TestWebServerLocalEps.AllocateFreeTcpEndPoint(TestHelper.TestIps.LocalNsTestIp);
        using var listener = new TcpListener(localEndPoint);
        listener.Start();
        var remoteEndPoint = new IPEndPoint(TestHelper.TestIps.RemoteNsTestIp, localEndPoint.Port);

        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var accessToken = await accessManager.AccessTokenService.Create(TestCt);
        var options = TestHelper.CreateClientOptions(accessManager.GetToken(accessToken));
        options.CanShowRewardedAdOverVpn = true;
        await using var client = await TestHelper.CreateClient(options);

        // around the tunnel first
        var session = (ClientSession)client.RequiredSession;
        var wait = session.AdHandler.WaitForAd(TestCt);
        Assert.AreEqual(AdRoute.Passthrough, session.Status.AdRoute);
        Assert.IsFalse(session.Status.IsLastAdRoute);
        Assert.IsTrue(session.PassthroughState.PassthroughForAd);

        // then through it, last: the try's traffic goes in the tunnel
        session.AdHandler.SetAdFailed(new InvalidOperationException("The first try fails for the test."),
            tryNextRoute: true);

        Assert.AreEqual(AdRoute.OverVpn, session.Status.AdRoute);
        Assert.IsTrue(session.Status.IsLastAdRoute);
        Assert.IsFalse(session.PassthroughState.PassthroughForAd);
        var passthruCount = session.Status.StreamPassthruCount;
        await TestHelper.Test_Https();
        Assert.AreEqual(passthruCount, session.Status.StreamPassthruCount);

        // a connection made during the try through the tunnel stays after the ad
        using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        ioCts.CancelAfter(TimeSpan.FromSeconds(20));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(remoteEndPoint, ioCts.Token);
        await tcpClient.GetStream().WriteAsync(new byte[] { 1 }, ioCts.Token);
        using var accepted = await listener.AcceptTcpClientAsync(ioCts.Token);
        await accepted.GetStream().ReadExactlyAsync(new byte[1], ioCts.Token);

        session.AdHandler.SetAdOk();
        await wait.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsNull(session.Status.AdRoute);
        await tcpClient.GetStream().WriteAsync(new byte[] { 2 }, ioCts.Token);
        var received = new byte[1];
        await accepted.GetStream().ReadExactlyAsync(received, ioCts.Token);
        Assert.AreEqual(2, received[0]);
    }

    // a failure no route helps, as an ad blocker the app refuses, ends the wait where a route was left
    [TestMethod]
    public async Task An_ad_failure_no_route_helps_ends_the_wait_at_once()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var accessToken = await accessManager.AccessTokenService.Create(TestCt);
        var options = TestHelper.CreateClientOptions(accessManager.GetToken(accessToken));
        options.CanShowRewardedAdOverVpn = true;
        await using var client = await TestHelper.CreateClient(options);

        var session = (ClientSession)client.RequiredSession;
        var wait = session.AdHandler.WaitForAd(TestCt);
        Assert.IsFalse(session.Status.IsLastAdRoute);

        var failure = new InvalidOperationException("An ad blocker for the test.");
        session.AdHandler.SetAdFailed(failure, tryNextRoute: false);
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            wait.WaitAsync(TimeSpan.FromSeconds(5), TestCt));

        Assert.AreSame(failure, ex);
        Assert.IsNull(session.Status.AdRoute);
        Assert.IsFalse(session.PassthroughState.PassthroughForAd);
    }

    [TestMethod]
    public async Task A_wait_for_an_ad_its_caller_canceled_already_turns_nothing_on()
    {
        // it took the session's traffic out of the tunnel, and its end reset the connections
        await using var server = await TestHelper.CreateServer();
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)));

        var session = (ClientSession)client.RequiredSession;
        var adHandler = session.AdHandler;
        var noticeCount = 0;
        adHandler.IsWaitingForChanged += (_, _) => Interlocked.Increment(ref noticeCount);

        using var canceledCts = new CancellationTokenSource();
        await canceledCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => adHandler.WaitForAd(canceledCts.Token));
        Assert.AreEqual(0, Volatile.Read(ref noticeCount));
        Assert.IsFalse(session.PassthroughState.PassthroughForAd);
    }

    [TestMethod]
    public async Task A_wait_for_an_ad_whose_notice_fails_ends()
    {
        // it stayed under way, with no owner to end it
        await using var server = await TestHelper.CreateServer();
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)));

        var session = (ClientSession)client.RequiredSession;
        var adHandler = session.AdHandler;
        var isFailing = true;
        adHandler.IsWaitingForChanged += (_, _) => {
            if (Volatile.Read(ref isFailing))
                throw new InvalidOperationException("A failed notice for the test.");
        };

        var wait = adHandler.WaitForAd(TestCt);
        Volatile.Write(ref isFailing, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => wait.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        Assert.IsFalse(adHandler.IsWaitingForAd);
        Assert.IsFalse(session.PassthroughState.PassthroughForAd);
    }

    [TestMethod]
    public async Task A_wait_for_an_ad_after_the_sessions_end_is_canceled_at_once()
    {
        await using var server = await TestHelper.CreateServer();
        var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)));

        var adHandler = ((ClientSession)client.RequiredSession).AdHandler;
        await client.DisposeAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            adHandler.WaitForAd(TestCt).WaitAsync(TimeSpan.FromSeconds(5), TestCt));
    }

    [TestMethod]
    public async Task A_caller_that_cancels_its_wait_for_an_ad_gets_its_cancellation()
    {
        await using var server = await TestHelper.CreateServer();
        await using var client = await TestHelper.CreateClient(
            TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server)));

        var adHandler = (SessionAdHandler)((ClientSession)client.RequiredSession).AdHandler;

        // it came back as a failed ad, which the session's start tries again
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var wait = adHandler.TryShowBeforeVpn(cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => wait.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
    }
}
