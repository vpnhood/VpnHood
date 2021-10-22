using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Common.Messaging;

namespace VpnHood.Test.Tests
{
    [TestClass]
    public class AccessTest
    {
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
            catch(Exception ex) when (ex is not AssertFailedException)
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
            catch(Exception ex) when (ex is not AssertFailedException)
            {
            }
        }

        [TestMethod]
        public async Task Server_reject_expired_access_hello()
        {
            using var server = TestHelper.CreateServer();

            // create an expired token
            var token = TestHelper.CreateAccessToken(server, expirationTime: DateTime.Now.AddDays(-1));

            // create client and connect
            using var client1 = TestHelper.CreateClient(token, autoConnect: false);
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
        public void Server_reject_expired_access_runtime()
        {
            using var server = TestHelper.CreateServer(accessSyncCacheSize: 50);

            // create an short expiring token
            var accessToken = TestHelper.CreateAccessToken(server, expirationTime: DateTime.Now.AddSeconds(1));

            // connect and download
            using var client1 = TestHelper.CreateClient(accessToken);

            try
            {
                Thread.Sleep(1200);
                TestHelper.Test_Https(timeout: 1000);
            }
            catch { /* ignored */ }

            TestHelper.WaitForClientState(client1, ClientState.Disposed);
            Assert.AreEqual(SessionErrorCode.AccessExpired, client1.SessionStatus.ErrorCode);
        }

        [TestMethod]
        public void Server_reject_trafficOverflow_access()
        {
            using var server = TestHelper.CreateServer(accessSyncCacheSize: 50);

            // create an fast expiring token
            var accessToken = TestHelper.CreateAccessToken(server, maxTrafficByteCount: 50);

            // ----------
            // check: client must disconnect at runtime on traffic overflow
            // ----------
            using var client1 = TestHelper.CreateClient(accessToken);
            Assert.AreEqual(50, client1.SessionStatus.AccessUsage?.MaxTraffic);

            // first try should just break the connection
            try
            {
                TestHelper.Test_Https();
            }
            catch
            {
                // ignored
            }

            Thread.Sleep(1000);
            // second try should get the AccessTrafficOverflow status
            try
            {
                TestHelper.Test_Https();
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
                using var client2 = TestHelper.CreateClient(accessToken);
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
        public void Server_maxClient_suppress_other_sessions()
        {
            using var packetCapture = TestHelper.CreatePacketCapture();

            // Create Server
            using var server = TestHelper.CreateServer(accessSyncCacheSize: 50);
            var token = TestHelper.CreateAccessToken(server, 2);

            // create default token with 2 client count
            using var client1 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
                clientId: Guid.NewGuid(), options: new ClientOptions {AutoDisposePacketCapture = false});

            // suppress by yourself
            using var client2 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
                clientId: client1.ClientId, options: new ClientOptions {AutoDisposePacketCapture = false});
            Assert.AreEqual(SessionSuppressType.YourSelf, client2.SessionStatus.SuppressedTo);
            Assert.AreEqual(SessionSuppressType.None, client2.SessionStatus.SuppressedBy);

            // new connection attempt will result to disconnect of client1
            try
            {
                TestHelper.Test_Https();
            }
            catch
            {
                // ignored
            }

            // wait for finishing client1
            TestHelper.WaitForClientState(client1, ClientState.Disposed);
            Assert.AreEqual(ClientState.Disposed, client1.State, "Client1 has not been stopped yet!");
            Assert.AreEqual(SessionSuppressType.None, client1.SessionStatus.SuppressedTo);
            Assert.AreEqual(SessionSuppressType.YourSelf, client1.SessionStatus.SuppressedBy);

            // suppress by other (MaxTokenClient is 2)
            using var client3 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
                clientId: Guid.NewGuid(), options: new ClientOptions {AutoDisposePacketCapture = false});
            using var client4 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
                clientId: Guid.NewGuid(), options: new ClientOptions {AutoDisposePacketCapture = false});

            // send a request to check first open client
            try
            {
                TestHelper.Test_Https();
            }
            catch
            {
                // ignored
            }

            // create a client with another token
            var accessTokenX = TestHelper.CreateAccessToken(server);
            using var clientX = TestHelper.CreateClient(packetCapture: packetCapture, clientId: Guid.NewGuid(),
                token: accessTokenX, options: new ClientOptions {AutoDisposePacketCapture = false});

            // send a request to check first open client
            try
            {
                TestHelper.Test_Https();
            }
            catch
            {
                // ignored
            }

            try
            {
                TestHelper.Test_Https();
            }
            catch
            {
                // ignored
            }

            // wait for finishing client2
            TestHelper.WaitForClientState(client2, ClientState.Disposed);
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
        public void Server_maxClient_should_not_suppress_when_zero()
        {
            using var packetCapture = TestHelper.CreatePacketCapture();

            // Create Server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessToken(server, 0);

            // client1
            using var client1 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
                clientId: Guid.NewGuid(), options: new ClientOptions {AutoDisposePacketCapture = false});
            using var client2 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
                clientId: Guid.NewGuid(), options: new ClientOptions {AutoDisposePacketCapture = false});

            // suppress by yourself
            using var client3 = TestHelper.CreateClient(packetCapture: packetCapture, token: token,
                clientId: Guid.NewGuid(), options: new ClientOptions {AutoDisposePacketCapture = false});
            Assert.AreEqual(SessionSuppressType.None, client3.SessionStatus.SuppressedTo);
            Assert.AreEqual(SessionSuppressType.None, client3.SessionStatus.SuppressedBy);
        }
    }
}