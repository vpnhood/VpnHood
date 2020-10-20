using VpnHood.Client;
using VpnHood.Messages;
using VpnHood.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

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
            var clientInfo = TestHelper.CreateDefaultClientInfo(server.TcpHostEndPoint.Port);
            clientInfo.Token.TokenId = Guid.NewGuid();

            try
            {
                using var client1 = TestHelper.CreateClient(clientInfo.Token.ServerEndPoint.Port, token: clientInfo.Token);
                Assert.Fail("Client should connect with invalid token id");
            }
            catch { }

            // ************
            // *** TEST ***: request with invalid token signature
            clientInfo = TestHelper.CreateDefaultClientInfo(server.TcpHostEndPoint.Port);
            clientInfo.Token.Secret = Guid.NewGuid().ToByteArray();

            try
            {
                using var client2 = TestHelper.CreateClient(clientInfo.Token.ServerEndPoint.Port);
                Assert.Fail("Client should connect with invalid token secret");
            }
            catch { }
        }

        [TestMethod]
        public void Server_suppress_other_sessions()
        {
            using var packetCapture = TestHelper.CreatePacketCapture();

            // Create Server
            using var server = TestHelper.CreateServer(tokenMaxClientCount: 2);

            // create default token with 2 client count
            using var client1 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);

            // suppress by yourself
            using var client2 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: client1.ClientId, leavePacketCaptureOpen: true);
            Assert.AreEqual(SuppressType.YourSelf, client2.SuppressedTo);
            Assert.AreEqual(SuppressType.None, client2.SuppressedBy);

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
            Assert.AreEqual(SuppressType.None, client1.SuppressedTo);
            Assert.AreEqual(SuppressType.YourSelf, client1.SuppressedBy);

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
            Assert.AreEqual(SuppressType.YourSelf, client2.SuppressedTo);
            Assert.AreEqual(SuppressType.Other, client2.SuppressedBy);
            Assert.AreEqual(SuppressType.None, client3.SuppressedBy);
            Assert.AreEqual(SuppressType.None, client3.SuppressedTo);
            Assert.AreEqual(SuppressType.Other, client4.SuppressedTo);
            Assert.AreEqual(SuppressType.None, client4.SuppressedBy);
        }

        [TestMethod]
        public void Server_dont_Suppress_when_maxClientCount_is_zero()
        {
            using var packetCapture = TestHelper.CreatePacketCapture();

            // Create Server
            using var server = TestHelper.CreateServer(tokenMaxClientCount: 0);

            // client1
            using var client1 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);
            using var client2 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);

            // suppress by yourself
            using var client3 = TestHelper.CreateClient(server.TcpHostEndPoint.Port, packetCapture, clientId: Guid.NewGuid(), leavePacketCaptureOpen: true);
            Assert.AreEqual(SuppressType.None, client3.SuppressedTo);
            Assert.AreEqual(SuppressType.None, client3.SuppressedBy);
        }
    }
}
