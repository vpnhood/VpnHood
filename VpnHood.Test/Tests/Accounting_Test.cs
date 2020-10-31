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
    public class Accounting_Test
    {
        [TestMethod]
        public void Server_reject_invalid_requests()
        {
            using var server = TestHelper.CreateServer();

            // ************
            // *** TEST ***: request with invalid tokenId
            var accessItem = TestHelper.CreateDefaultAccessItem(server.TcpHostEndPoint.Port);

            try
            {
                using var client1 = TestHelper.CreateClient(IPEndPoint.Parse(accessItem.Token.ServerEndPoint).Port, token: accessItem.Token);
                Assert.Fail("Client should connect with invalid token id");
            }
            catch { }

            // ************
            // *** TEST ***: request with invalid token signature
            accessItem = TestHelper.CreateDefaultAccessItem(server.TcpHostEndPoint.Port);
            accessItem.Token.Secret = Guid.NewGuid().ToByteArray();

            try
            {
                using var client2 = TestHelper.CreateClient(IPEndPoint.Parse(accessItem.Token.ServerEndPoint).Port);
                Assert.Fail("Client should connect with invalid token secret");
            }
            catch { }
        }

        [TestMethod]
        public void Server_reject_expired_access_hello()
        {
            using var server = TestHelper.CreateServer();

            // ************
            // *** TEST ***: request with expired token
            
            // create an expired token
            var accessItem = TestHelper.CreateDefaultAccessItem(server.TcpHostEndPoint.Port);
            accessItem.ExpirationTime = DateTime.Now.AddDays(-1);
            var accessServer = (FileAccessServer)server.SessionManager.AccessServer;
            accessServer.AddAccessItem(accessItem);

            // create client and connect
            using var client1 = TestHelper.CreateClient(server.TcpHostEndPoint.Port);
            try
            {
                client1.Connect().Wait();
                Assert.Fail("Exception expected! access has been expired");
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
            }


            //traffic expired
            //todo
        }

        [TestMethod]
        public void Server_reject_expired_access_runtime()
        {
            //time expired

            //traffic expired
            throw new NotImplementedException();
        }

        [TestMethod]
        public void Server_reject_trafficOverflow_access_hello()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        public void Server_reject_trafficOverflow_access_runtime()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        public void Server_suppress_other_sessions()
        {
            using var packetCapture = TestHelper.CreatePacketCapture();

            // Create Server
            using var server = TestHelper.CreateServer(maxClient: 2);

            // create default token with 2 client count
            using var client1 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);

            // suppress by yourself
            using var client2 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: client1.ClientId, leavePacketCaptureOpen: true);
            Assert.AreEqual(SuppressType.YourSelf, client2.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.None, client2.SessionStatus.SuppressedBy);

            // new connection attempt my result to disconnect of client1
            try
            {
                using var httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromMilliseconds(1000)
                };
                var _ = httpClient.GetStringAsync("https://www.quad9.net/").Result;
            }
            catch { };

            // wait for finishing client1
            TestHelper.WaitForClientToDispose(client1);
            Assert.AreEqual(ClientState.Disposed, client1.State, "Client1 has not been stopped yet!");
            Assert.AreEqual(SuppressType.None, client1.SessionStatus.SuppressedTo);
            Assert.AreEqual(SuppressType.YourSelf, client1.SessionStatus.SuppressedBy);

            // suppress by other (MaxTokenClient is 2)
            using var client3 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);
            using var client4 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);
            try
            {
                using var httpClient = new HttpClient()
                {
                    Timeout = TimeSpan.FromMilliseconds(1000)
                };
                _ = httpClient.GetStringAsync("https://www.quad9.net/").Result;
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
        }

        [TestMethod]
        public void Server_dont_Suppress_when_maxClientCount_is_zero()
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
