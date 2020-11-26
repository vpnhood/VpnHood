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
using VpnHood.Loggers;

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

        private static int _accessItemIndex = 0;
        public static FileAccessServer.AccessItem CreateAccessItem(VpnHoodServer server,
            int maxClientCount = 1,
            int maxTrafficByteCount = 0,
            DateTime? expirationTime = null
            )
        {
            var accessServer = (FileAccessServer)server.AccessServer;
            return accessServer.CreateAccessItem(
                serverEndPoint: new IPEndPoint(IPAddress.Parse("127.0.0.1"), server.TcpHostEndPoint.Port),
                tokenName: $"Test Server {++_accessItemIndex}",
                maxClientCount: maxClientCount,
                maxTrafficByteCount: maxTrafficByteCount,
                expirationTime: expirationTime
                );
        }

        public static VpnHoodServer CreateServer()
        {
            Logger.Current = Logger.CreateConsoleLogger(true);
            var accessServer = new FileAccessServer(Path.Combine(WorkingPath, $"AccessServer_{Guid.NewGuid()}"));

            // Create server
            var server = new VpnHoodServer(accessServer, new ServerOptions()
            {
                TcpHostEndPoint = TestUtil.GetFreeEndPoint(),
                TcpClientFactory = new TestTcpClientFactory(),
                UdpClientFactory = new TestUdpClientFactory()
            });

            server.Start().Wait();
            Assert.AreEqual(ServerState.Started, server.State);

            return server;
        }

        public static IDevice CreateDevice() => new TestDevice(GetTestIpAddresses());
        public static IPacketCapture CreatePacketCapture() => new TestDevice(GetTestIpAddresses()).CreatePacketCapture().Result;

        public static VpnHoodClient CreateClient(
            Token token,
            IPacketCapture packetCapture = null,
            Guid? clientId = null,
            bool leavePacketCaptureOpen = false,
            bool autoConnect = true)
        {
            if (packetCapture == null) packetCapture = CreatePacketCapture();
            if (clientId == null) clientId = Guid.NewGuid();

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
            if (autoConnect)
                client.Connect().Wait();
            return client;
        }
    }
}
