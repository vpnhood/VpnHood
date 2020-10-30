using VpnHood.Server;
using VpnHood.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.IO;
using VpnHood.Test.Factory;
using VpnHood.Server.AccessServers;

namespace VpnHood.Test
{
    static class TestHelper
    {
        public static string WorkingPath { get; } = Path.Combine(Environment.CurrentDirectory, "_TestWorkingPath");

        internal static void Cleanup()
        {
            try
            {
                if (Directory.Exists(WorkingPath))
                    Directory.Delete(WorkingPath, true);
            }
            catch { }
        }

        public static void WaitForClientToDispose(VpnHoodClient client, int timeout = 4000)
        {
            var waitTime = 500;
            for (var elapsed = 0; elapsed < timeout && client.State != ClientState.Disposed; elapsed += waitTime)
                Thread.Sleep(waitTime);
        }

        public static IPAddress TcpProxyLoopbackAddress => IPAddress.Parse("10.255.255.255");
        private static IPAddress[] GetTestIpAddresses()
        {
            var addresses = new List<IPAddress>();
            addresses.AddRange(Dns.GetHostAddresses("www.quad9.net"));
            addresses.Add(IPAddress.Parse("9.9.9.9"));
            addresses.Add(TcpProxyLoopbackAddress);
            return addresses.ToArray();
        }

        public static FileAccessServer.AccessItem CreateDefaultAccessItem(int serverPort)
        {
            var certificate = new X509Certificate2("certs/test.vpnhood.com.pfx", "1");

            var ret = new FileAccessServer.AccessItem()
            {
                 MaxClient = 1,
                Token = new Token()
                {
                    Name = "Default Test Server",
                    DnsName = certificate.GetNameInfo(X509NameType.DnsName, false),
                    PublicKeyHash = Token.ComputePublicKeyHash(certificate.GetPublicKey()),
                    Secret = new byte[16] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
                    SupportId = 1,
                    TokenId = Guid.Parse("{7E57453D-387C-47F3-864C-7B79B89E65F7}"),
                    ServerEndPoint = $"127.0.0.1:{serverPort}",
                }
            };

            return ret;
        }

        public static VpnHoodServer CreateServer(int maxClient = 1)
        {
            var accessServer = new FileAccessServer(Path.Combine(WorkingPath, $"AccessServer_{Guid.NewGuid()}"));

            // Create server
            var server = new VpnHoodServer(accessServer, new ServerOptions()
            {
                TcpHostEndPoint = TestUtil.GetFreeEndPoint(),
                Certificate = new X509Certificate2("certs/test.vpnhood.com.pfx", "1"),
                TcpClientFactory = new TestTcpClientFactory(),
                UdpClientFactory = new TestUdpClientFactory()
            });

            var accessItem = CreateDefaultAccessItem(server.TcpHostEndPoint.Port);
            accessItem.MaxClient = maxClient;
            accessServer.AddAccessItem(accessItem);

            server.Start().Wait();
            Assert.AreEqual(ServerState.Started, server.State);

            return server;
        }

        public static IDevice CreateDevice() => new TestDevice(GetTestIpAddresses());
        public static IPacketCapture CreatePacketCapture() => new TestDevice(GetTestIpAddresses()).CreatePacketCapture().Result;

        public static VpnHoodClient CreateClient(int serverPort,
            IPacketCapture packetCapture = null,
            Token token = null,
            Guid? clientId = null,
            bool leavePacketCaptureOpen = false)
        {
            if (packetCapture == null) packetCapture = CreatePacketCapture();
            if (clientId == null) clientId = Guid.NewGuid();
            if (token == null) token = CreateDefaultAccessItem(serverPort).Token;

            var client = new VpnHoodClient(
              packetCapture: packetCapture,
              clientId: clientId.Value,
              token: token,
              new ClientOptions()
              {
                  TcpIpChannelCount = 4,
                  IpResolveMode = IpResolveMode.DnsThenToken,
                  TcpProxyLoopbackAddress = TcpProxyLoopbackAddress,
                  LeavePacketCaptureOpen = leavePacketCaptureOpen
              });

            // test starting the client
            client.Connect().Wait();
            return client;
        }
    }
}
