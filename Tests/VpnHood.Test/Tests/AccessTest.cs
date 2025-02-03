﻿using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;
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
        Assert.AreEqual(SessionErrorCode.AccessExpired, client.ConnectionInfo.ErrorCode);
    }

    [TestMethod]
    public async Task Server_reject_expired_access_at_runtime()
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.SyncInterval = TimeSpan.FromMinutes(10); // make sure the disconnect is not due to sync
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create a short expiring token
        var accessToken = TestHelper.CreateAccessToken(server, expirationTime: DateTime.Now.AddSeconds(1));

        // connect and download
        await using var client = await TestHelper.CreateClient(accessToken, throwConnectException: false);

        // test expiration
        await VhTestUtil.AssertEqualsWait(ClientState.Disposed, async () => {
            await TestHelper.Test_Https(throwError: false, timeout: 1000);
            return client.State;
        });
        Assert.AreEqual(SessionErrorCode.AccessExpired, client.ConnectionInfo.ErrorCode);
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

        VhLogger.Instance.LogTrace("Test: second try should get the AccessTrafficOverflow status.");
        await VhTestUtil.AssertEqualsWait(SessionErrorCode.AccessTrafficOverflow, async () => {
            await TestHelper.Test_Https(timeout: 2000, throwError: false);
            return client1.ConnectionInfo.ErrorCode;
        });


        // ----------
        // check: client must disconnect at hello on traffic overflow
        // ----------
        try {
            VhLogger.Instance.LogTrace("Test: try to connect with another client.");
            await using var client2 = await TestHelper.CreateClient(accessToken);
            Assert.Fail("Exception expected! Traffic must been overflowed!");
        }
        catch (AssertFailedException) {
            throw;
        }
        catch {
            Assert.AreEqual(SessionErrorCode.AccessTrafficOverflow, client1.ConnectionInfo.ErrorCode);
        }
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
        await using var client1 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(),
            token: token);

        // suppress by yourself
        await using var client2 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(),
            token: token, clientId: client1.ClientId);

        Assert.AreEqual(SessionSuppressType.YourSelf, client2.ConnectionInfo.SessionInfo?.SuppressedTo);
        Assert.IsNull(client2.ConnectionInfo.Error);

        // wait for finishing client1
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Waiting for client1 disposal.");
        await TestHelper.WaitForClientState(client1, ClientState.Disposed, useUpdateStatus: true);
        Assert.AreEqual(SessionSuppressType.None, client1.ConnectionInfo.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, client1.ConnectionInfo.ErrorCode);
        Assert.AreEqual(nameof(SessionSuppressType.YourSelf), client1.ConnectionInfo.Error?.Data[nameof(SessionResponse.SuppressedBy)]);

        // suppress by other (MaxTokenClient is 2)
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Creating client3.");
        await using var client3 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(),
            token: token);

        await using var client4 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(),
            token: token);

        // create a client with another token
        var accessTokenX = TestHelper.CreateAccessToken(server);
        await using var clientX = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(),
            token: accessTokenX);

        // wait for finishing client2
        VhLogger.Instance.LogTrace(GeneralEventId.Test, "Test: Waiting for client2 disposal.");
        await TestHelper.WaitForClientState(client2, ClientState.Disposed, useUpdateStatus: true);

        Assert.AreEqual(SessionSuppressType.YourSelf, client2.ConnectionInfo.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionErrorCode.SessionSuppressedBy, client2.ConnectionInfo.ErrorCode);
        Assert.AreEqual(nameof(SessionSuppressType.Other), client2.ConnectionInfo.Error?.Data[nameof(SessionResponse.SuppressedBy)]);
        Assert.AreEqual(SessionErrorCode.Ok, client3.ConnectionInfo.ErrorCode);
        Assert.AreEqual(SessionSuppressType.None, client3.ConnectionInfo.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionSuppressType.Other, client4.ConnectionInfo.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionErrorCode.Ok, client4.ConnectionInfo.ErrorCode);
        Assert.AreEqual(SessionErrorCode.Ok, clientX.ConnectionInfo.ErrorCode);
        Assert.AreEqual(SessionSuppressType.None, clientX.ConnectionInfo.SessionInfo?.SuppressedTo);
    }

    [TestMethod]
    public async Task Server_maxClient_should_not_suppress_when_zero()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server, 0);

        // client1
        await using var client1 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(),
            token: token);

        await using var client2 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(),
            token: token);

        await using var client3 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(),
            token: token);

        Assert.AreEqual(SessionSuppressType.None, client3.ConnectionInfo.SessionInfo?.SuppressedTo);
        Assert.AreEqual(SessionErrorCode.Ok, client3.ConnectionInfo.ErrorCode);
    }

    [TestMethod]
    public async Task Client_should_not_suppress_itself_after_disconnect()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create default token with 2 client count
        var client1 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(),
            token: token);
        await client1.DisposeAsync();
        await TestHelper.WaitForClientState(client1, ClientState.Disposed);


        // suppress by yourself
        await using var client2 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(),
            token: token, clientId: client1.ClientId);

        Assert.AreEqual(SessionErrorCode.Ok, client1.ConnectionInfo.ErrorCode);
        Assert.AreEqual(SessionSuppressType.None, client2.ConnectionInfo.SessionInfo?.SuppressedTo);
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
        var client1 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(), token: token);
        await client1.DisposeAsync(waitForBye: true);
        await TestHelper.WaitForClientState(client1, ClientState.Disposed);
        await server.SessionManager.Sync(true);

        await Task.Delay(1000);
        var time = DateTime.UtcNow;
        // suppress by yourself
        await using var client2 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(), token: token);
        var accessInfo = client2.ConnectionInfo.SessionInfo?.AccessInfo;
        Assert.IsNotNull(accessInfo);
        Assert.IsNull(client1.ConnectionInfo.Error);
        Assert.AreEqual(SessionSuppressType.None, client2.ConnectionInfo.SessionInfo?.SuppressedTo);
        Assert.AreEqual(2, accessInfo.MaxDeviceCount);
        Assert.AreEqual(2_000_000, accessInfo.MaxTotalTraffic);
        Assert.AreEqual(true, accessInfo.IsPremium);
        Assert.AreEqual(expired, accessInfo.ExpirationTime);
        Assert.IsTrue(accessInfo.CreatedTime < time);
        Assert.IsTrue(accessInfo.LastUsedTime < time);
        await client2.DisposeAsync(waitForBye: true);
        await TestHelper.WaitForClientState(client2, ClientState.Disposed);
        await server.SessionManager.Sync(true);

        await Task.Delay(1000);
        await using var client3 = await TestHelper.CreateClient(packetCapture: new TestNullPacketCapture(), token: token);
        accessInfo = client3.ConnectionInfo.SessionInfo?.AccessInfo;
        Assert.IsNotNull(accessInfo);
        Assert.IsNotNull(accessInfo);
        Assert.IsTrue(accessInfo.CreatedTime < time);
        Assert.IsTrue(accessInfo.LastUsedTime > time);
    }

}