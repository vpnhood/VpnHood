using VpnHood.Client;
using VpnHood.Messages;
using VpnHood.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using VpnHood.Server.AccessServers;

namespace VpnHood.Test
{

    [TestClass]
    public class Access_Test
    {
        [TestMethod]
        public void Server_reject_invalid_requests()
        {
            using var server = TestHelper.CreateServer();

            // ************
            // *** TEST ***: request with invalid tokenId
            var accessItem = TestHelper.CreateDefaultAccessItem(server.TcpHostEndPoint.Port);
            accessItem.Token.TokenId = Guid.NewGuid(); //set invalid tokenId

            try
            {
                using var client1 = TestHelper.CreateClient(IPEndPoint.Parse(accessItem.Token.ServerEndPoint).Port, token: accessItem.Token);
                Assert.Fail("Client should not connect with invalid token id");
            }
            catch (AssertFailedException) { throw; }
            catch { }

            // ************
            // *** TEST ***: request with invalid token signature
            accessItem = TestHelper.CreateDefaultAccessItem(server.TcpHostEndPoint.Port);
            accessItem.Token.Secret = Guid.NewGuid().ToByteArray(); //set invalid secret

            try
            {
                using var client2 = TestHelper.CreateClient(serverPort: 0, token: accessItem.Token);
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
            var accessItem = TestHelper.CreateDefaultAccessItem(server.TcpHostEndPoint.Port);
            accessItem.ExpirationTime = DateTime.Now.AddDays(-1);
            var accessServer = (FileAccessServer)server.SessionManager.AccessServer;
            accessServer.AddAccessItem(accessItem);

            // create client and connect
            using var client1 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, autoConnect: false);
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

            // create an fast expiring token
            var accessServer = (FileAccessServer)server.SessionManager.AccessServer;
            var accessItem = TestHelper.CreateDefaultAccessItem(server.TcpHostEndPoint.Port);
            accessItem.ExpirationTime = DateTime.Now.AddSeconds(1);
            accessServer.AddAccessItem(accessItem);

            // connect and download
            using var client1 = TestHelper.CreateClient(server.TcpHostEndPoint.Port);

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
            var accessServer = (FileAccessServer)server.SessionManager.AccessServer;
            var accessItem = TestHelper.CreateDefaultAccessItem(server.TcpHostEndPoint.Port);
            accessItem.MaxTrafficByteCount = 50;
            accessServer.AddAccessItem(accessItem);

            // ----------
            // check: client must disconnect at runtime on traffic overflow
            // ----------
            using var client1 = TestHelper.CreateClient(server.TcpHostEndPoint.Port);

            try
            {
                using var httpClient1 = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) };
                httpClient1.GetStringAsync("https://www.quad9.net").Wait();
                
                using var httpClient2 = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) };
                httpClient2.GetStringAsync("https://www.quad9.net").Wait();
                Assert.Fail("Exception expected! Traffic must been overflowed!");
            }
            catch (AssertFailedException) { throw; }
            catch 
            {
                Assert.AreEqual(ResponseCode.AccessTrafficOverflow, client1.SessionStatus?.ResponseCode);
            }

            // ----------
            // check: client must disconnect at hello on traffic overflow
            // ----------
            try
            {
                using var client2 = TestHelper.CreateClient(server.TcpHostEndPoint.Port);
                Assert.Fail("Exception expected! Traffic must been overflowed!");
            }
            catch (AssertFailedException) { throw; }
            catch
            {
                Assert.AreEqual(ResponseCode.AccessTrafficOverflow, client1.SessionStatus?.ResponseCode);
            }

        }

        [TestMethod] //todo accessId is not implemented, this test should be failed
        public void Server_maxClient_suppress_other_sessions()
        {
            using var packetCapture = TestHelper.CreatePacketCapture();

            // Create Server
            using var server = TestHelper.CreateServer(maxClient: 2);
            var fileAccessServer = (FileAccessServer)server.SessionManager.AccessServer;

            // create default token with 2 client count
            using var client1 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);

            // suppress by yourself
            using var client2 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: client1.ClientId, leavePacketCaptureOpen: true);
            Assert.AreEqual(SuppressType.YourSelf, client2.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.None, client2.SessionStatus.SuppressedBy);

            // new connection attempt my result to disconnect of client1
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) };
                httpClient.GetStringAsync("https://www.quad9.net/").Wait();
            }
            catch { };

            // wait for finishing client1
            TestHelper.WaitForClientToDispose(client1);
            Assert.AreEqual(ClientState.IsDisposed, client1.State, "Client1 has not been stopped yet!");
            Assert.AreEqual(SuppressType.None, client1.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.YourSelf, client1.SessionStatus.SuppressedBy);

            // suppress by other (MaxTokenClient is 2)
            using var client3 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);
            using var client4 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);

            // send a request to check first open client
            try
            {
                using var httpClient = new HttpClient() { Timeout = TimeSpan.FromMilliseconds(1000) };
                httpClient.GetStringAsync("https://www.quad9.net/").Wait();
            }
            catch { }

            // create a client with another token
            var accessItemX = TestHelper.CreateDefaultAccessItem(server.TcpHostEndPoint.Port);
            accessItemX.Token.TokenId = Guid.NewGuid();
            accessItemX.Token.Name = "tokenX";
            fileAccessServer.AddAccessItem(accessItemX);
            using var clientX = TestHelper.CreateClient(0, packetCapture, clientId: Guid.NewGuid(), token: accessItemX.Token, leavePacketCaptureOpen: true);

            // send a request to check first open client
            try
            {
                using var httpClient = new HttpClient() { Timeout = TimeSpan.FromMilliseconds(1000) };
                httpClient.GetStringAsync("https://www.quad9.net/").Wait();
            }
            catch { }

            // wait for finishing client2
            TestHelper.WaitForClientToDispose(client2);
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
            using var server = TestHelper.CreateServer(maxClient: 0);

            // client1
            using var client1 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);
            using var client2 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);

            // suppress by yourself
            using var client3 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);
            Assert.AreEqual(SuppressType.None, client3.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.None, client3.SessionStatus.SuppressedBy);
        }
    }
}
