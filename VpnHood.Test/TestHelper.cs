using VpnHood.Server;
using VpnHood.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.IO;
using System.Security.Cryptography;
using VpnHood.Test.Factory;

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

        public static TokenInfo CreateDefaultTokenInfo(int serverPort)
        {
            var certificate = new X509Certificate2("certs/test.vpnhood.com.pfx", "1");

            return new TokenInfo()
            {
                MaxClientCount = 1,
                Token = new Token()
                {
                    Name = "Default Test Server",
                    DnsName = certificate.GetNameInfo(X509NameType.DnsName, false),
                    PublicKeyHash = Token.ComputePublicKeyHash(certificate.GetPublicKey()),
                    Secret = new byte[16] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
                    SupportId = 1,
                    TokenId = Guid.Parse("{7E57453D-387C-47F3-864C-7B79B89E65F7}"),
                    ServerEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), serverPort),
                }
            };
        }

        public static VpnHoodServer CreateServer(int tokenMaxClientCount = 1)
        {
            var tokenStore = new FileTokenStore(Path.Combine(WorkingPath, $"TokenStore_{Guid.NewGuid()}"));

            // Create server
            var server = new VpnHoodServer(tokenStore, new ServerOptions()
            {
                TcpHostEndPoint = TestUtil.GetFreeEndPoint(),
                Certificate = new X509Certificate2("certs/test.vpnhood.com.pfx", "1"),
                TcpClientFactory = new TcpClientFactoryTest(),
                UdpClientFactory = new UdpClientFactoryTest()
            });

            var tokenInfo = CreateDefaultTokenInfo(server.TcpHostEndPoint.Port);
            tokenInfo.MaxClientCount = tokenMaxClientCount;
            tokenStore.AddToken(tokenInfo);

            server.Start().Wait();
            Assert.AreEqual(ServerState.Started, server.State);

            return server;
        }

        public static IDeviceInbound CreateDevice() => new WinDivertDeviceTest(GetTestIpAddresses());

        public static VpnHoodClient CreateClient(int serverPort,
            IDeviceInbound device = null,
            bool leaveDeviceOpen = false,
            Token token = null,
            Guid? clientId = null)
        {
            if (device == null) device = CreateDevice();
            if (clientId == null) clientId = Guid.NewGuid();
            if (token == null) token = CreateDefaultTokenInfo(serverPort).Token;

            var client = new VpnHoodClient(
              device: device,
              clientId: clientId.Value,
              token: token,
              new ClientOptions()
              {
                  TcpIpChannelCount = 4,
                  IpResolveMode = IpResolveMode.DnsThenToken,
                  TcpProxyLoopbackAddress = TcpProxyLoopbackAddress,
                  LeaveDeviceOpen = leaveDeviceOpen
              });

            // test starting the client
            client.Connect().Wait();
            return client;
        }
    }
}
