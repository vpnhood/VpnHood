using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Server.Configurations;

namespace VpnHood.Test.Tests;

[TestClass]
public class AccessTest
{

    [TestMethod]
    public async Task Foo()
    {
        await Task.Delay(100);
        var file = Environment.GetFolderPath(Environment.SpecialFolder.System);
        Console.WriteLine(file);

    }

    [TestInitialize]
    public void Initialize()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
    }

    [TestMethod]
    public void Server_reject_invalid_requests()
    {
        using var server = TestHelper.CreateServer();

        // ************
        // *** TEST ***: request with invalid tokenId
        var token = TestHelper.CreateAccessToken(server);
        token.TokenId = Guid.NewGuid(); //set invalid tokenId

        try
        {
            using var client1 = TestHelper.CreateClient(token);
            Assert.Fail("Client should not connect with invalid token id");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
        }

        // ************
        // *** TEST ***: request with invalid token signature
        token = TestHelper.CreateAccessToken(server);
        token.Secret = Guid.NewGuid().ToByteArray(); //set invalid secret

        try
        {
            using var client2 = TestHelper.CreateClient(token);
            Assert.Fail("Client should not connect with invalid token secret");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
        }
    }

    [TestMethod]
    public async Task Server_reject_expired_access_hello()
    {
        await using var server = TestHelper.CreateServer();

        // create an expired token
        var token = TestHelper.CreateAccessToken(server, expirationTime: DateTime.Now.AddDays(-1));

        // create client and connect
        await using var client1 = TestHelper.CreateClient(token, autoConnect: false);
        try
        {
            await client1.Connect();
            Assert.Fail("Exception expected! access has been expired");
        }
        catch (AssertFailedException)
        {
            throw;
        }
        catch
        {
            Assert.AreEqual(SessionErrorCode.AccessExpired, client1.SessionStatus.ErrorCode);
        }
    }

    [TestMethod]
    public async Task Server_reject_expired_access_at_runtime()
    {
        var fileAccessServerOptions = TestHelper.CreateFileAccessServerOptions();
        fileAccessServerOptions.SessionOptions.SyncInterval = TimeSpan.FromMilliseconds(200);
        await using var server = TestHelper.CreateServer(fileAccessServerOptions);

        // create an short expiring token
        var accessToken = TestHelper.CreateAccessToken(server, expirationTime: DateTime.Now.AddSeconds(1));

        // connect and download
        await using var client1 = TestHelper.CreateClient(accessToken);

        await TestHelper.AssertEqualsWait(ClientState.Disposed, async () =>
        {
            await TestHelper.Test_HttpsAsync(throwError: false);
            return client1.State;
        });
        Assert.AreEqual(SessionErrorCode.AccessExpired, client1.SessionStatus.ErrorCode);
    }

    [TestMethod]
    public async Task Server_reject_trafficOverflow_access()
    {
        await using var server = TestHelper.CreateServer();

        // create an fast expiring token
        var accessToken = TestHelper.CreateAccessToken(server, maxTrafficByteCount: 50);

        // ----------
        // check: client must disconnect at runtime on traffic overflow
        // ----------
        await using var client1 = TestHelper.CreateClient(accessToken);
        Assert.AreEqual(50, client1.SessionStatus.AccessUsage?.MaxTraffic);

        // first try should just break the connection
        try
        {
            await TestHelper.Test_HttpsAsync();
        }
        catch
        {
            // ignored
        }

        Thread.Sleep(1000);
        // second try should get the AccessTrafficOverflow status
        try
        {
            await TestHelper.Test_HttpsAsync();
        }
        catch
        {
            // ignored
        }

        Assert.AreEqual(SessionErrorCode.AccessTrafficOverflow, client1.SessionStatus.ErrorCode);

        // ----------
        // check: client must disconnect at hello on traffic overflow
        // ----------
        try
        {
            await using var client2 = TestHelper.CreateClient(accessToken);
            Assert.Fail("Exception expected! Traffic must been overflowed!");
        }
        catch (AssertFailedException)
        {
            throw;
        }
        catch
        {
            Assert.AreEqual(SessionErrorCode.AccessTrafficOverflow, client1.SessionStatus.ErrorCode);
        }
    }

    [TestMethod]
    public async Task Server_maxClient_suppress_other_sessions()
    {
        using var packetCapture = TestHelper.CreatePacketCapture();

        // Create Server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server, 2);

        // create default token with 2 client count
        await using var client1 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
            clientId: Guid.NewGuid(), options: new ClientOptions { AutoDisposePacketCapture = false });

        // suppress by yourself
        await using var client2 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
            clientId: client1.ClientId, options: new ClientOptions { AutoDisposePacketCapture = false });

        Assert.AreEqual(SessionSuppressType.YourSelf, client2.SessionStatus.SuppressedTo);
        Assert.AreEqual(SessionSuppressType.None, client2.SessionStatus.SuppressedBy);

        await TestHelper.AssertEqualsWait(ClientState.Disposed, async () =>
        {
            await TestHelper.Test_HttpsAsync(throwError: false);
            return client1.State;
        }, "Client1 has not been stopped yet.");
        Assert.AreEqual(SessionSuppressType.None, client1.SessionStatus.SuppressedTo);
        Assert.AreEqual(SessionSuppressType.YourSelf, client1.SessionStatus.SuppressedBy);

        // suppress by other (MaxTokenClient is 2)
        await using var client3 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
            clientId: Guid.NewGuid(), options: new ClientOptions { AutoDisposePacketCapture = false });

        await using var client4 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
            clientId: Guid.NewGuid(), options: new ClientOptions { AutoDisposePacketCapture = false });

        // create a client with another token
        var accessTokenX = TestHelper.CreateAccessToken(server);
        await using var clientX = TestHelper.CreateClient(packetCapture: packetCapture, clientId: Guid.NewGuid(),
            token: accessTokenX, options: new ClientOptions { AutoDisposePacketCapture = false });


        // wait for finishing client2
        await TestHelper.AssertEqualsWait(ClientState.Disposed, async () =>
        {
            await TestHelper.Test_HttpsAsync(throwError: false);
            return client2.State;
        });
        Assert.AreEqual(SessionSuppressType.YourSelf, client2.SessionStatus.SuppressedTo);
        Assert.AreEqual(SessionSuppressType.Other, client2.SessionStatus.SuppressedBy);
        Assert.AreEqual(SessionSuppressType.None, client3.SessionStatus.SuppressedBy);
        Assert.AreEqual(SessionSuppressType.None, client3.SessionStatus.SuppressedTo);
        Assert.AreEqual(SessionSuppressType.Other, client4.SessionStatus.SuppressedTo);
        Assert.AreEqual(SessionSuppressType.None, client4.SessionStatus.SuppressedBy);
        Assert.AreEqual(SessionSuppressType.None, clientX.SessionStatus.SuppressedBy);
        Assert.AreEqual(SessionSuppressType.None, clientX.SessionStatus.SuppressedTo);
    }

    [TestMethod]
    public async Task Server_maxClient_should_not_suppress_when_zero()
    {

        // Create Server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server, 0);

        // client1
        using var packetCapture = TestHelper.CreatePacketCapture();
        await using var client1 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
            clientId: Guid.NewGuid(), options: new ClientOptions { AutoDisposePacketCapture = false });

        await using var client2 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
            clientId: Guid.NewGuid(), options: new ClientOptions { AutoDisposePacketCapture = false });

        // suppress by yourself
        await using var client3 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
            clientId: Guid.NewGuid(), options: new ClientOptions { AutoDisposePacketCapture = false });

        Assert.AreEqual(SessionSuppressType.None, client3.SessionStatus.SuppressedTo);
        Assert.AreEqual(SessionSuppressType.None, client3.SessionStatus.SuppressedBy);
    }
}