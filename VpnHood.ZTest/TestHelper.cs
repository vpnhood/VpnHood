using VpnHood.Server;
using VpnHood.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.IO;
using VpnHood.Test.Factory;
using VpnHood.Server.AccessServers;
using VpnHood.Logging;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using VpnHood.Client.App;
using System.Net.Http;
using VpnHood.Common;
using VpnHood.Client.Device;

namespace VpnHood.Test
{
    static class TestHelper
    {
        public static readonly Uri TEST_Uri = new Uri("https://www.quad9.net/");
        public static readonly IPEndPoint TEST_NsEndPoint = IPEndPoint.Parse("9.9.9.9:53");
        public static readonly IPAddress TEST_NsEndAddress = IPAddress.Parse("9.9.9.9");
        public static readonly Uri TEST_InvalidUri = new Uri("https://DBBC5764-D452-468F-8301-4B315507318F.zz");
        public static readonly IPAddress TEST_InvalidIp = IPAddress.Parse("192.168.199.199");
        public static readonly IPEndPoint TEST_InvalidEp = Util.ParseIpEndPoint("192.168.199.199:9999");
        public static readonly IPAddress TcpProxyLoopbackAddress = IPAddress.Parse("10.255.255.255");

        private class TestAppProvider : IAppProvider
        {
            public IDevice Device { get; } = CreateDevice();
            public string OperatingSystemInfo =>
                Environment.OSVersion.ToString() + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
        }

        public static string WorkingPath { get; } = Path.Combine(Path.GetTempPath(), "_test_vpnhood");

        public static string CreateNewFolder(string namePart)
        {
            string folder = Path.Combine(WorkingPath, $"{namePart}_{Guid.NewGuid()}");
            Directory.CreateDirectory(folder);
            return folder;
        }

        internal static void Cleanup()
        {
            try
            {
                if (Directory.Exists(WorkingPath))
                    Directory.Delete(WorkingPath, true);
            }
            catch { }
        }

        public static void WaitForClientState(VpnHoodApp app, ClientState clientState, int timeout = 4000)
        {
            if (clientState == ClientState.Disposed)
                throw new ArgumentException("ClientState for app never set to dispose, check none instead.", nameof(clientState));

            var waitTime = 200;
            for (var elapsed = 0; elapsed < timeout && app.State.ClientState != clientState; elapsed += waitTime)
                Thread.Sleep(waitTime);
        }

        public static void WaitForClientState(VpnHoodClient client, ClientState clientState, int timeout = 6000)
        {
            var waitTime = 200;
            for (var elapsed = 0; elapsed < timeout && client.State != clientState; elapsed += waitTime)
                Thread.Sleep(waitTime);
        }

        public static PingReply SendPing(Ping ping = null, int timeout = 5000)
        {
            using var pingT = new Ping();
            if (ping == null) ping = pingT;
            var pingOptions = new PingOptions()
            {
                Ttl = TestPacketCapture.ServerPingTtl // set ttl to control by test adapter
            };

            return ping.Send(TEST_NsEndAddress, timeout, new byte[100], pingOptions);
        }

        public static IPHostEntry SendUdp(UdpClient udpClient = null, int timeout = 5000)
        {
            return TestUtil.GetHostEntry("www.google.com", TEST_NsEndPoint, udpClient, timeout);
        }

        public static bool SendHttpGet()
        {
            using var httpClient = new HttpClient();
            var result = httpClient.GetStringAsync(TEST_Uri).Result;
            return result.Length > 100;
        }

        private static IPAddress[] GetTestIpAddresses()
        {
            var addresses = new List<IPAddress>();
            addresses.AddRange(Dns.GetHostAddresses(TEST_Uri.Host));
            addresses.Add(TEST_NsEndAddress);
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

        public static VpnHoodServer CreateServer(IAccessServer accessServer = null, IPEndPoint tcpHostEndPoint = null)
        {
            VhLogger.Current = VhLogger.CreateConsoleLogger(true);
            if (accessServer == null)
                accessServer = new FileAccessServer(Path.Combine(WorkingPath, $"AccessServer_{Guid.NewGuid()}"));

            // Create server
            var server = new VpnHoodServer(accessServer, new ServerOptions()
            {
                TcpHostEndPoint = tcpHostEndPoint ?? TestUtil.GetFreeEndPoint(),
                TcpClientFactory = new TestTcpClientFactory(),
                UdpClientFactory = new TestUdpClientFactory()
            });

            server.Start().Wait();
            Assert.AreEqual(ServerState.Started, server.State);

            return server;
        }

        public static IDevice CreateDevice() => new TestDevice(GetTestIpAddresses());
        public static IPacketCapture CreatePacketCapture() => new TestDevice(GetTestIpAddresses()).CreatePacketCapture().Result;

        public static VpnHoodClient CreateClient(Token token,
            IPacketCapture packetCapture = null,
            Guid? clientId = null,
            bool autoConnect = true,
            ClientOptions options = null)
        {
            if (options == null) options = new ClientOptions();
            if (packetCapture == null) packetCapture = CreatePacketCapture();
            if (clientId == null) clientId = Guid.NewGuid();
            if (options.TcpProxyLoopbackAddress == null) options.TcpProxyLoopbackAddress = TcpProxyLoopbackAddress;
            if (options.Timeout == new ClientOptions().Timeout) options.Timeout = 2000; //overwrite default timeout

            var client = new VpnHoodClient(
              packetCapture: packetCapture,
              clientId: clientId.Value,
              token: token,
              options);

            // test starting the client
            if (autoConnect)
                client.Connect().Wait();
            return client;
        }

        public static VpnHoodApp CreateClientApp(string appPath = null)
        {
            //create app
            var appOptions = new AppOptions()
            {
                AppDataPath = appPath ?? Path.Combine(WorkingPath, "AppData_" + Guid.NewGuid()),
                LogToConsole = true,
                Timeout = 2000,
            };
            
            var clientApp = VpnHoodApp.Init(new TestAppProvider(), appOptions);
            clientApp.Diagnoser.PingTtl = TestPacketCapture.ServerPingTtl;
            clientApp.Diagnoser.HttpTimeout = 2000;
            clientApp.Diagnoser.NsTimeout = 2000;

            return clientApp;
        }

    }
}
