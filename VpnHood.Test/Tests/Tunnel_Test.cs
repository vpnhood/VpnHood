using System;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using VpnHood.Server;
using VpnHood.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Sockets;

namespace VpnHood.Test
{

    [TestClass]
    public class Tunnel_Test
    {
        [ClassInitialize]
        public static void Init(TestContext _)
        {
        }

        [TestMethod]
        public void Proxy_tunnel()
        {
            // Create Server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessItem(server).Token;
            Assert.AreEqual(ServerState.Started, server.State);

            // Create Client
            using var client = TestHelper.CreateClient(token: token);
            Assert.AreEqual(ClientState.Connected, client.State);

            // Get session
            var serverSession = server.SessionManager.FindSessionByClientId(client.ClientId);
            Assert.IsNotNull(serverSession, "Could not find session in server!");

            var oldClientSentByteCount = client.SentByteCount;
            var oldClientReceivedByteCount = client.ReceivedByteCount;
            var oldServerSentByteCount = serverSession.Tunnel.SentByteCount;
            var oldServerReceivedByteCount = serverSession.Tunnel.ReceivedByteCount;

            // ************
            // *** TEST ***: TCP (TLS) by quad9
            using var httpClient = new HttpClient();
            var result = httpClient.GetStringAsync("https://www.quad9.net/").Result;
            Assert.IsTrue(result.Length > 2);

            // check there is send data
            Assert.IsTrue(client.SentByteCount > oldClientSentByteCount + 100, "Not enough data has been sent through the client!");
            Assert.IsTrue(client.ReceivedByteCount > oldClientReceivedByteCount + 2000, "Not enough data has been sent through the client!");
            Assert.IsTrue(serverSession.Tunnel.SentByteCount > oldServerSentByteCount + 2000, "Not enough data has been sent through the client!");
            Assert.IsTrue(serverSession.Tunnel.ReceivedByteCount > oldServerReceivedByteCount + 100, "Not enough data has been sent through the client!");

            // ************
            // *** TEST ***: UDP
            oldClientSentByteCount = client.SentByteCount;
            oldClientReceivedByteCount = client.ReceivedByteCount;
            oldServerSentByteCount = serverSession.Tunnel.SentByteCount;
            oldServerReceivedByteCount = serverSession.Tunnel.ReceivedByteCount;

            Test_Udp();

            Assert.IsTrue(client.SentByteCount > oldClientSentByteCount + 10, "Not enough data has been sent through the client!");
            Assert.IsTrue(client.ReceivedByteCount > oldClientReceivedByteCount + 10, "Not enough data has been sent through the client!");
            Assert.IsTrue(serverSession.Tunnel.SentByteCount > oldServerSentByteCount + 10, "Not enough data has been sent through the client!");
            Assert.IsTrue(serverSession.Tunnel.ReceivedByteCount > oldServerReceivedByteCount + 10, "Not enough data has been sent through the client!");

            // ************
            // *** TEST ***: Icmp
            oldClientSentByteCount = client.SentByteCount;
            oldClientReceivedByteCount = client.ReceivedByteCount;
            oldServerSentByteCount = serverSession.Tunnel.SentByteCount;
            oldServerReceivedByteCount = serverSession.Tunnel.ReceivedByteCount;

            Test_Icmp();

            Assert.IsTrue(client.SentByteCount > oldClientSentByteCount + 100, "Not enough data has been sent through the client!");
            Assert.IsTrue(client.ReceivedByteCount > oldClientReceivedByteCount + 100, "Not enough data has been sent through the client!");
            Assert.IsTrue(serverSession.Tunnel.SentByteCount > oldServerSentByteCount + 100, "Not enough data has been sent through the client!");
            Assert.IsTrue(serverSession.Tunnel.ReceivedByteCount > oldServerReceivedByteCount + 100, "Not enough data has been sent through the client!");
        }

        [TestMethod]
        public void Client_must_despose_after_device_closed()
        {
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessItem(server).Token;

            using var packetCapture = TestHelper.CreatePacketCapture();
            using var client = TestHelper.CreateClient(token: token, packetCapture: packetCapture);

            packetCapture.StopCapture();
            Assert.AreEqual(ClientState.Disposed, client.State);
        }

        [TestMethod]
        public void Datagram_channel_after_client_reconnection()
        {
            //create a shared udp client among connection
            // make sure using same local port to test Nat properly
            using var udpClient = new UdpClient();
            using var ping = new Ping();

            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessItem(server).Token;

            // create client
            using var client1 = TestHelper.CreateClient(token: token);

            // test Icmp & Udp
            Test_Icmp(ping);
            Test_Udp(udpClient);

            // create client
            using var client2 = TestHelper.CreateClient(token: token);

            // test Icmp & Udp
            Test_Icmp(ping);
            Test_Udp(udpClient);
        }

        private void Test_Icmp(Ping ping = null)
        {
            using var pingT = new Ping();
            if (ping == null) ping = pingT;
            var pingReply = ping.Send("9.9.9.9", 5000, new byte[100], new PingOptions()
            { Ttl = TestPacketCapture.ServerTimeToLife }); // set ttl to control by test adapter
            Assert.AreEqual(IPStatus.Success, pingReply.Status);
        }

        private void Test_Udp(UdpClient udpClient = null)
        {
            var hostEntry = TestUtil.GetHostEntry("www.google.com", IPEndPoint.Parse("9.9.9.9:53"), udpClient);
            Assert.IsNotNull(hostEntry);
            Assert.IsTrue(hostEntry.AddressList.Length > 0);
        }


        [ClassCleanup]
        public static void ClassCleanup()
        {
        }
    }
}
