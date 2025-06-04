using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Test.Device;

namespace VpnHood.Test.Tests;

[TestClass]
public class AccessTest : TestBase
{
    [TestMethod]
    public async Task Server_reject_invalid_requests()
    {
        await using var server = await TestHelper.CreateServer();

        // ************
        // *** TEST ***: request with invalid tokenId
        var token = TestHelper.CreateAccessToken(server);
        token.TokenId = Guid.NewGuid().ToString(); //set invalid tokenId

        try {
            await using var client1 = await TestHelper.CreateClient(token);
            Assert.Fail("Client should not connect with invalid token id");
        }
        catch (Exception ex) when (ex is not AssertFailedException) {
        }

        // ************
        // *** TEST ***: request with invalid token signature
        token = TestHelper.CreateAccessToken(server);
        token.Secret = Guid.NewGuid().ToByteArray(); //set invalid secret

        try {
            await using var client2 = await TestHelper.CreateClient(token);
            Assert.Fail("Client should not connect with invalid token secret");
        }
        catch (Exception ex) when (ex is not AssertFailedException) {
        }
    }

    [TestMethod]
    public async Task Server_reject_expired_access_hello()
    {
        await using var server = await TestHelper.CreateServer();

        // create an expired token
        var token = TestHelper.CreateAccessToken(server, expirationTime: DateTime.Now.AddDays(-1));

        // create client and connect
        await using var client = await TestHelper.CreateClient(token, autoConnect: false);
        await Assert.ThrowsExceptionAsync<SessionException>(() => client.Connect());
        Assert.AreEqual(SessionErrorCode.AccessExpired, client.GetLastSessionErrorCode());
    }

    [TestMethod]
    public async Task Server_reject_expired_access_at_runtime()
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.SyncInterval = TimeSpan.FromMinutes(10); // make sure the disconnect is not due to sync
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create a short expiring token
        var accessToken = TestHelper.CreateAccessToken(server, expirationTime: DateTime.UtcNow.AddSeconds(1));

        // connect and download
        await using var client = await TestHelper.CreateClient(accessToken);

        // test expiration
        await VhTestUtil.AssertEqualsWait(ClientState.Disposed, async () => {
            await server.SessionManager.Sync(true);
            await TestHelper.Test_Https(throwError: false, timeout: 500);
            return client.State;
        });
        Assert.AreEqual(SessionErrorCode.AccessExpired, client.GetLastSessionErrorCode());
    }

    [TestMethod]
    public async Task Server_reject_trafficOverflow_access()
    {
        // create server
        var managerOptions = TestHelper.CreateFileAccessManagerOptions();
        managerOptions.SessionOptions.SyncInterval = TimeSpan.FromMilliseconds(200);
        await using var server = await TestHelper.CreateServer(managerOptions);

        // create a fast expiring token
        var accessToken = TestHelper.CreateAccessToken(server, maxTrafficByteCount: 50);

        // ----------
        // check: client must disconnect at runtime on traffic overflow
        // ----------
        await using var client1 = await TestHelper.CreateClient(accessToken);
        Assert.AreEqual(50, client1.GetSessionStatus().SessionMaxTraffic);

        VhLogger.Instance.LogDebug("Test: second try should get the AccessTrafficOverflow status.");
        await VhTestUtil.AssertEqualsWait(SessionErrorCode.AccessTrafficOverflow, async () => {
            await TestHelper.Test_Https(timeout: 2000, throwError: false);
            return client1.GetLastSessionErrorCode();
        });


        // ----------
        // check: client must disconnect at hello on traffic overflow
        // ----------
        await using var client2 = await TestHelper.CreateClient(accessToken, autoConnect: false);
        var ex = await Assert.ThrowsExceptionAsync<SessionException>(() => client2.Connect());
        Assert.AreEqual(SessionErrorCode.AccessTrafficOverflow, client1.GetLastSessionErrorCode());
        Assert.AreEqual(SessionErrorCode.AccessTrafficOverflow, ex.SessionResponse.ErrorCode);
    }

    [TestMethod]
    public async Task Server_maxClient_suppress_other_sessions()
    {
        var managerOptions = TestHelper.CreateFileAccessManagerOptions();
        managerOptions.SessionOptions.SyncInterval = TimeSpan.FromMilliseconds(200);

        // Create Server
        await using var server = await TestHelper.CreateServer(managerOptions);
        var token = TestHelper.CreateAccessToken(server, maxClientCount: 2);

        // create default token with 2 client count
        await using var client1 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: token);

        // suppress by yourself
        await using var client2 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: token, clientId: client1.ClientId);

        Assert.AreEqual(SessionSuppressType.YourSelf, client2.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionErrorCode.Ok, client2.GetLastSessionErrorCode());

        // wait for finishing client1
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Waiting for client1 disposal.");
        await client1.WaitForState(ClientState.Disposed, useUpdateStatus: true);
        Assert.AreEqual(SessionSuppressType.None, client1.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, client1.GetLastSessionErrorCode());
        Assert.AreEqual(SessionSuppressType.YourSelf,
            (client1.LastException as SessionException)?.SessionResponse.SuppressedBy);

        // suppress by other (MaxTokenClient is 2)
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Creating client3.");
        await using var client3 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: token);

        await using var client4 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: token);

        // create a client with another token
        var accessTokenX = TestHelper.CreateAccessToken(server);
        await using var clientX = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: accessTokenX);

        // wait for finishing client2
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Waiting for client2 disposal.");
        await client2.WaitForState(ClientState.Disposed, useUpdateStatus: true);

        Assert.AreEqual(SessionSuppressType.YourSelf, client2.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, client2.GetLastSessionErrorCode());
        Assert.AreEqual(SessionSuppressType.Other, (client2.LastException as SessionException)?.SessionResponse.SuppressedBy);
        Assert.AreEqual(SessionErrorCode.Ok, client3.GetLastSessionErrorCode());
        Assert.AreEqual(SessionSuppressType.None, client3.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionSuppressType.Other, client4.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionErrorCode.Ok, client4.GetLastSessionErrorCode());
        Assert.AreEqual(SessionErrorCode.Ok, clientX.GetLastSessionErrorCode());
        Assert.AreEqual(SessionSuppressType.None, clientX.SessionInfo?.SuppressedTo);
    }

    [TestMethod]
    public async Task Server_maxClient_should_not_suppress_when_zero()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server, 0);

        // client1
        await using var client1 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: token);

        await using var client2 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: token);

        await using var client3 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: token);

        Assert.AreEqual(SessionSuppressType.None, client3.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionErrorCode.Ok, client3.GetLastSessionErrorCode());
    }

    [TestMethod]
    public async Task Client_should_not_suppress_itself_after_disconnect()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create default token with 2 client count
        var client1 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: token);
        await client1.DisposeAsync();
        await client1.WaitForState(ClientState.Disposed);


        // suppress by yourself
        await using var client2 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: token, clientId: client1.ClientId);

        Assert.AreEqual(SessionErrorCode.Ok, client1.GetLastSessionErrorCode());
        Assert.AreEqual(SessionSuppressType.None, client2.SessionInfo?.SuppressedTo);
    }

    [TestMethod]
    public async Task Client_should_get_access_info()
    {
        // Create Server
        var expired = DateTime.UtcNow.AddDays(1);
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server, maxClientCount: 2,
            expirationTime: expired,
            maxTrafficByteCount: 2_000_000);

        // create default token with 2 client count
        await using (var client1 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token)) {
            await client1.WaitForState(ClientState.Connected);
        }

        await server.SessionManager.Sync(true);
        await Task.Delay(1000);
        // remove milliseconds from time for comparison
        var time = DateTime.UtcNow;
        time = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);

        // create a new client with the same token, it should not suppress
        await using (var client2 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token)) {
            var accessInfo = client2.SessionInfo?.AccessInfo;
            Assert.IsNotNull(accessInfo);
            Assert.IsNull(client2.LastException);
            Assert.AreEqual(SessionSuppressType.None, client2.SessionInfo?.SuppressedTo);
            Assert.AreEqual(2, accessInfo.MaxDeviceCount);
            Assert.AreEqual(2_000_000, accessInfo.MaxTotalTraffic);
            Assert.AreEqual(true, accessInfo.IsPremium);
            Assert.AreEqual(expired, accessInfo.ExpirationTime);
            Assert.IsTrue(accessInfo.CreatedTime <= time);
            Assert.IsTrue(accessInfo.LastUsedTime <= time, $"Diff: {(time - accessInfo.LastUsedTime).TotalSeconds}sec");
        }

        await server.SessionManager.Sync(true);
        await Task.Delay(200);

        // create a new client with the same token, it should not suppress
        await using (var client3 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token))
        {
            var accessInfo = client3.SessionInfo?.AccessInfo;
            Assert.IsNotNull(accessInfo);
            Assert.IsNotNull(accessInfo);
            Assert.IsTrue(accessInfo.CreatedTime < time);
            Assert.IsTrue(accessInfo.LastUsedTime > time);
        }
    }
}