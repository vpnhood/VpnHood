using VpnHood.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net.Http;
using System.Threading;
using VpnHood.Server.AccessServers;
using VpnHood.Tunneling.Messages;

namespace VpnHood.Test
{
    [TestClass]
    public class Test_Access
    {
        [TestMethod]
        public void Server_reject_invalid_requests()
        {
            using var server = TestHelper.CreateServer();

            // ************
            // *** TEST ***: request with invalid tokenId
            var token = TestHelper.CreateAccessItem(server).Token;
            token.TokenId = Guid.NewGuid(); //set invalid tokenId

            try
            {
                using var client1 = TestHelper.CreateClient(token: token);
                Assert.Fail("Client should not connect with invalid token id");
            }
            catch (AssertFailedException) { throw; }
            catch { }

            // ************
            // *** TEST ***: request with invalid token signature
            token = TestHelper.CreateAccessItem(server).Token;
            token.Secret = Guid.NewGuid().ToByteArray(); //set invalid secret

            try
            {
                using var client2 = TestHelper.CreateClient(token: token);
                Assert.Fail("Client should not connect with invalid token secret");
            }
            catch (AssertFailedException) { throw; }
            catch { }
        }

        [TestMethod]
        public void Server_reject_expired_access_hello()
        {
            using var server = TestHelper.CreateServer();

            // create an expired token
            var token = TestHelper.CreateAccessItem(server, expirationTime: DateTime.Now.AddDays(-1)).Token;

            // create client and connect
            using var client1 = TestHelper.CreateClient(token: token, autoConnect: false);
            try
            {
                client1.Connect().Wait();
                Assert.Fail("Exception expected! access has been expired");
            }
            catch (AssertFailedException) { throw; }
            catch
            {
                Assert.AreEqual(ResponseCode.AccessExpired, client1.SessionStatus?.ResponseCode);
            }
        }

        [TestMethod]
        public void Server_reject_expired_access_runtime()
        {
            using var server = TestHelper.CreateServer();

            // create an short expiring token
            var accessItem = TestHelper.CreateAccessItem(server, expirationTime: DateTime.Now.AddSeconds(1));

            // connect and download
            using var client1 = TestHelper.CreateClient(token: accessItem.Token);

            try
            {
                Thread.Sleep(1200);
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) };
                httpClient.GetStringAsync("https://www.quad9.net/").Wait();
                Assert.Fail("Exception expected! Access must been expired!");
            }
            catch (AssertFailedException) { throw; }
            catch
            {
                Assert.AreEqual(ResponseCode.AccessExpired, client1.SessionStatus?.ResponseCode);
            }
        }

        [TestMethod]
        public void Server_reject_trafficOverflow_access()
        {
            using var server = TestHelper.CreateServer();

            // create an fast expiring token
            var accessServer = (FileAccessServer)server.AccessServer;
            var accessItem = TestHelper.CreateAccessItem(server, maxTrafficByteCount: 50);

            // ----------
            // check: client must disconnect at runtime on traffic overflow
            // ----------
            using var client1 = TestHelper.CreateClient(token: accessItem.Token);
            Assert.AreEqual(50, client1.SessionStatus.AccessUsage.MaxTrafficByteCount);

            try
            {
                using var httpClient1 = new HttpClient ();
                httpClient1.GetStringAsync("https://www.quad9.net").Wait();

                using var httpClient2 = new HttpClient ();
                httpClient2.GetStringAsync("https://www.quad9.net").Wait();
                Assert.Fail("Exception expected! Traffic must been overflowed!");
            }
            catch (AssertFailedException) { throw; }
            catch (Exception ex)
            {
                Assert.AreEqual(ResponseCode.AccessTrafficOverflow, client1.SessionStatus?.ResponseCode);
            }

            // ----------
            // check: client must disconnect at hello on traffic overflow
            // ----------
            try
            {
                using var client2 = TestHelper.CreateClient(token: accessItem.Token);
                Assert.Fail("Exception expected! Traffic must been overflowed!");
            }
            catch (AssertFailedException) { throw; }
            catch
            {
                Assert.AreEqual(ResponseCode.AccessTrafficOverflow, client1.SessionStatus?.ResponseCode);
            }
        }

        [TestMethod]
        public void Server_maxClient_suppress_other_sessions()
        {
            using var packetCapture = TestHelper.CreatePacketCapture();

            // Create Server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessItem(server, maxClientCount: 2).Token;

            // create default token with 2 client count
            using var client1 = TestHelper.CreateClient(packetCapture: packetCapture, token: token, clientId: Guid.NewGuid(), options: new ClientOptions() { LeavePacketCaptureOpen = true });

            // suppress by yourself
            using var client2 = TestHelper.CreateClient(packetCapture: packetCapture, token: token, clientId: client1.ClientId, options: new ClientOptions() { LeavePacketCaptureOpen = true });
            Assert.AreEqual(SuppressType.YourSelf, client2.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.None, client2.SessionStatus.SuppressedBy);

            // new connection attempt will result to disconnect of client1
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) };
                httpClient.GetStringAsync("https://www.quad9.net/").Wait();
            }
            catch { };

            // wait for finishing client1
            TestHelper.WaitForClientState(client1, ClientState.Disposed);
            Assert.AreEqual(ClientState.Disposed, client1.State, "Client1 has not been stopped yet!");
            Assert.AreEqual(SuppressType.None, client1.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.YourSelf, client1.SessionStatus.SuppressedBy);

            // suppress by other (MaxTokenClient is 2)
            using var client3 = TestHelper.CreateClient(packetCapture: packetCapture, token: token, clientId: Guid.NewGuid(), options: new() { LeavePacketCaptureOpen = true });
            using var client4 = TestHelper.CreateClient(packetCapture: packetCapture, token: token, clientId: Guid.NewGuid(), options: new() { LeavePacketCaptureOpen = true });

            // send a request to check first open client
            try
            {
                using var httpClient = new HttpClient() { Timeout = TimeSpan.FromMilliseconds(1000) };
                httpClient.GetStringAsync("https://www.quad9.net/").Wait();
            }
            catch { }

            // create a client with another token
            var accessItemX = TestHelper.CreateAccessItem(server);
            using var clientX = TestHelper.CreateClient(packetCapture: packetCapture, clientId: Guid.NewGuid(), token: accessItemX.Token, options: new() { LeavePacketCaptureOpen = true });

            // send a request to check first open client
            try
            {
                using var httpClient = new HttpClient() { Timeout = TimeSpan.FromMilliseconds(1000) };
                httpClient.GetStringAsync("https://www.quad9.net/").Wait();
            }
            catch { }

            // wait for finishing client2
            TestHelper.WaitForClientState(client1, ClientState.Disposed);
            Assert.AreEqual(SuppressType.YourSelf, client2.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.Other, client2.SessionStatus.SuppressedBy);
            Assert.AreEqual(SuppressType.None, client3.SessionStatus.SuppressedBy);
            Assert.AreEqual(SuppressType.None, client3.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.Other, client4.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.None, client4.SessionStatus.SuppressedBy);
            Assert.AreEqual(SuppressType.None, clientX.SessionStatus.SuppressedBy);
            Assert.AreEqual(SuppressType.None, clientX.SessionStatus.SuppressedTo);
        }

        [TestMethod]
        public void Server_maxClient_dont_Suppress_when_zero()
        {
            using var packetCapture = TestHelper.CreatePacketCapture();

            // Create Server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessItem(server, maxClientCount: 0).Token;

            // client1
            using var client1 = TestHelper.CreateClient(packetCapture: packetCapture, token: token, clientId: Guid.NewGuid(), options: new ClientOptions() { LeavePacketCaptureOpen = true });
            using var client2 = TestHelper.CreateClient(packetCapture: packetCapture, token: token, clientId: Guid.NewGuid(), options: new ClientOptions() { LeavePacketCaptureOpen = true });

            // suppress by yourself
            using var client3 = TestHelper.CreateClient(packetCapture: packetCapture, token: token, clientId: Guid.NewGuid(), options: new ClientOptions() { LeavePacketCaptureOpen = true });
            Assert.AreEqual(SuppressType.None, client3.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.None, client3.SessionStatus.SuppressedBy);
        }
    }
}
